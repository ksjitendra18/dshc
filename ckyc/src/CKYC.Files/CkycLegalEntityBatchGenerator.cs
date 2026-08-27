using System.IO.Compression;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using CKYC.Core.Spec;

namespace CKYC.Files;

/// <summary>
/// Builds a CKYC legal-entity bulk-upload batch (step 4): validates each legal-entity
/// record against the conditional-mandatory file-format rules, writes the pipe-delimited
/// .UPL file for the client type "L", and packages it with the supporting documents into
/// a zip file. Mirrors <see cref="CkycBatchGenerator"/> for the individual client type.
/// </summary>
public sealed class CkycLegalEntityBatchGenerator : ILegalEntityBatchGenerator
{
    private readonly BatchSettings _batch;
    private readonly IFileHasher _hasher;
    private readonly IDocumentStore _documents;

    public CkycLegalEntityBatchGenerator(BatchSettings batch, IFileHasher hasher, IDocumentStore documents)
    {
        _batch = batch;
        _hasher = hasher;
        _documents = documents;
    }

    public async Task<GeneratedBatch> GenerateAsync(IReadOnlyList<LegalEntity> records, DateOnly businessDate, CancellationToken ct = default)
    {
        if (records.Count == 0)
            throw new InvalidOperationException("No legal-entity records supplied to the batch generator.");
        if (records.Count > CkycRecords.MaxLegalEntityBatchRecords)
            throw new InvalidOperationException($"A legal-entity batch cannot contain more than {CkycRecords.MaxLegalEntityBatchRecords} customers.");

        var (valid, skipped) = Partition(records, _batch.FiCode);
        if (valid.Count == 0)
            throw new InvalidOperationException(
                $"All {records.Count} legal-entity record(s) failed validation — no batch was produced. " +
                FormatValidationFailures(skipped));

        var descriptors = valid.Select(Describe).ToList();
        var (documentPlan, missing) = await BatchDocumentPlanner.CreateAsync(_documents, descriptors, ct);
        ApplyDocumentChecks(valid, skipped, documentPlan, missing);
        if (valid.Count == 0)
            throw new InvalidOperationException($"All {records.Count} legal-entity record(s) failed validation or document checks. " + FormatValidationFailures(skipped));
        descriptors = valid.Select(Describe).ToList();
        (documentPlan, _) = await BatchDocumentPlanner.CreateAsync(_documents, descriptors, ct);

        var fileName = CkycFileName.Build("L", _batch.UserId, _batch.FiCode, businessDate, _batch.SequenceStart, "UPL");
        var batchKey = Path.GetFileNameWithoutExtension(fileName);

        var batchDir = Path.Combine(_batch.OutputRoot, batchKey);
        var uploadDir = Path.Combine(batchDir, "upload");
        var docDir = Path.Combine(uploadDir, "support_docs");
        Directory.CreateDirectory(uploadDir);
        Directory.CreateDirectory(docDir);

        var writer = new CkycLegalEntityUploadWriter(_batch, documentPlan.Map);
        var content = writer.Write(valid, businessDate);
        var record20Lines = CkycLegalEntityUploadWriter.ComputeRecord20Lines(valid);

        var uploadPath = Path.Combine(uploadDir, fileName);
        File.WriteAllText(uploadPath, content, new UTF8Encoding(false));

        await documentPlan.MaterializeAsync(descriptors, docDir, ct);

        var zipPath = Path.Combine(batchDir, $"{batchKey}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        CreateArchive(uploadPath, docDir,
            valid.SelectMany(r => DocumentReferences.For(r).Select(name => documentPlan.Map(r.CustomerId, name)!)), zipPath);

        return new GeneratedBatch(batchKey, fileName, uploadPath, zipPath, valid.Count, DateTime.UtcNow, skipped, record20Lines);
    }

    private static (List<LegalEntity> Valid, List<SkippedRecord> Skipped) Partition(IReadOnlyList<LegalEntity> records, string fiCode)
    {
        var valid = new List<LegalEntity>();
        var skipped = new List<SkippedRecord>();

        foreach (var r in records)
        {
            var errors = LegalEntityRecordValidator.Validate(r).ToList();
            // The FVU requires the record-70 Institution Code to match the FI code used
            // in the batch file name (ERR_395).
            if (!string.Equals(r.Other?.InstitutionCode?.Trim(), fiCode.Trim(), StringComparison.OrdinalIgnoreCase))
                errors.Add(new ValidationError(null, "70", null, "Institution Code", r.Other?.InstitutionCode, null,
                    $"The Institution Code specified in the uploaded file must match the Institution Code used in the file name ({fiCode})."));
            if (errors.Count == 0)
            {
                valid.Add(r);
            }
            else
            {
                skipped.Add(new SkippedRecord(r.CustomerId, r.EntityName, errors));
            }
        }

        return (valid, skipped);
    }

    private static (long MasterId, string CustomerId, IReadOnlySet<string> References) Describe(LegalEntity record)
        => (record.MasterRecordId, record.CustomerId, DocumentReferences.For(record));

    private static string FormatValidationFailures(IEnumerable<SkippedRecord> skipped) =>
        string.Join(" ", skipped.Select(s =>
            $"{s.CustomerId}: {string.Join("; ", s.Errors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}"))}"));

    private static void ApplyDocumentChecks(List<LegalEntity> valid, List<SkippedRecord> skipped,
        BatchDocumentPlanner plan, IReadOnlyDictionary<long, List<string>> missing)
    {
        foreach (var record in valid.ToList())
        {
            var documents = DocumentReferences.For(record);
            var unsafeDocument = documents.FirstOrDefault(doc => !IsSafeDocumentName(doc));
            var missingFiles = missing.GetValueOrDefault(record.MasterRecordId);
            if (unsafeDocument is not null || missingFiles is { Count: > 0 })
            {
                valid.Remove(record);
                var value = unsafeDocument ?? string.Join(", ", missingFiles!);
                var documentMessage = unsafeDocument is not null
                    ? "A supporting document must be a PDF, JPG or JPEG file name without a directory path."
                    : $"The following supporting documents have not been imported: {value}.";
                skipped.Add(new SkippedRecord(record.CustomerId, record.EntityName,
                    [new ValidationError(null, "DOC", null, "Supporting documents", value, null, documentMessage)]));
                continue;
            }
            var total = documents.Sum(doc => plan.Get(record.MasterRecordId, doc).ByteLength);
            var oversizedSmallDocument = SmallDocuments(record)
                .FirstOrDefault(doc => plan.Get(record.MasterRecordId, doc).ByteLength > CkycRecords.MaxLegalSmallDocumentBytes);
            if (total <= CkycRecords.MaxLegalEntityBytesPerCustomer && oversizedSmallDocument is null) continue;

            valid.Remove(record);
            var message = oversizedSmallDocument is not null
                ? $"Document '{oversizedSmallDocument}' exceeds the workbook's 500 KB limit."
                : $"Supporting documents total {total} bytes; the per-customer legal-entity limit is {CkycRecords.MaxLegalEntityBytesPerCustomer} bytes (25 MB).";
            skipped.Add(new SkippedRecord(record.CustomerId, record.EntityName,
                [new ValidationError(null, "DOC", null, "Supporting documents", total.ToString(), null, message)]));
        }
    }

    private static IEnumerable<string> SmallDocuments(LegalEntity record)
    {
        foreach (var value in new[] { record.PanDocument, record.TinGstnDocument,
                     record.RegisteredAddressDocument, record.PrincipalAddressDocument })
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
    }

    private static bool IsSafeDocumentName(string document) =>
        !Path.IsPathRooted(document)
        && string.Equals(Path.GetFileName(document), document, StringComparison.Ordinal)
        && new[] { ".pdf", ".jpg", ".jpeg" }.Contains(Path.GetExtension(document), StringComparer.OrdinalIgnoreCase);

    private static void CreateArchive(string uploadPath, string docDir, IEnumerable<string> documents, string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(uploadPath, $"upload/{Path.GetFileName(uploadPath)}", CompressionLevel.Optimal);
        foreach (var document in documents.Distinct(StringComparer.OrdinalIgnoreCase))
            archive.CreateEntryFromFile(Path.Combine(docDir, document), $"upload/support_docs/{document}", CompressionLevel.Optimal);
    }
}
