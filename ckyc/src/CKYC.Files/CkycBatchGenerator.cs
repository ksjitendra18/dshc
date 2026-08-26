using System.IO.Compression;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using CKYC.Core.Spec;

namespace CKYC.Files;

/// <summary>
/// Builds the CKYC bulk-upload batch (step 4): validates each record against the
/// conditional-mandatory file-format rules, writes the pipe-delimited .UPL file for the
/// records that pass, and packages it with the supporting documents into a zip file.
///
/// Records that fail validation are excluded from the batch (never written to the .UPL)
/// and reported on the returned <see cref="GeneratedBatch.Skipped"/> list, so a broken
/// record cannot reach the FVU or the supporting-document zip.
/// </summary>
public sealed class CkycBatchGenerator : IBatchGenerator
{
    private readonly BatchSettings _batch;
    private readonly IFileHasher _hasher;
    private readonly IDocumentStore _documents;

    public CkycBatchGenerator(BatchSettings batch, IFileHasher hasher, IDocumentStore documents)
    {
        _batch = batch;
        _hasher = hasher;
        _documents = documents;
    }

    public async Task<GeneratedBatch> GenerateAsync(IReadOnlyList<Individual> records, DateOnly businessDate, CancellationToken ct = default)
    {
        if (records.Count == 0)
            throw new InvalidOperationException("No records supplied to the batch generator.");
        if (records.Count > CkycRecords.MaxIndividualBatchRecords)
            throw new InvalidOperationException($"An individual batch cannot contain more than {CkycRecords.MaxIndividualBatchRecords} customers.");

        // Validate every record first (the CM rules). Invalid records are skipped & reported.
        var (valid, skipped) = Partition(records);
        if (valid.Count == 0)
            throw new InvalidOperationException(
                $"All {records.Count} record(s) failed validation — no batch was produced. " +
                $"{FormatValidationFailures(skipped)}");

        var descriptors = valid.Select(Describe).ToList();
        var (documentPlan, missing) = await BatchDocumentPlanner.CreateAsync(_documents, descriptors, ct);
        ApplyDocumentChecks(valid, skipped, documentPlan, missing);
        if (valid.Count == 0)
            throw new InvalidOperationException($"All {records.Count} record(s) failed validation or document checks. " + FormatValidationFailures(skipped));

        // Re-plan after exclusions so filename collision allocation depends only on emitted records.
        descriptors = valid.Select(Describe).ToList();
        (documentPlan, _) = await BatchDocumentPlanner.CreateAsync(_documents, descriptors, ct);

        var fileName = CkycFileName.Build(_batch.ClientType, _batch.UserId, _batch.FiCode, businessDate, _batch.SequenceStart, "UPL");
        var batchKey = Path.GetFileNameWithoutExtension(fileName);

        var batchDir = Path.Combine(_batch.OutputRoot, batchKey);
        var uploadDir = Path.Combine(batchDir, "upload");
        var docDir = Path.Combine(uploadDir, "support_docs");
        Directory.CreateDirectory(uploadDir);
        Directory.CreateDirectory(docDir);

        var writer = new CkycUploadWriter(_batch, documentPlan.Map);
        var content = writer.Write(valid, businessDate);
        var record20Lines = CkycUploadWriter.ComputeRecord20Lines(valid);

        var uploadPath = Path.Combine(uploadDir, fileName);
        File.WriteAllText(uploadPath, content, new UTF8Encoding(false));

        await documentPlan.MaterializeAsync(descriptors, docDir, ct);

        var zipPath = Path.Combine(batchDir, $"{batchKey}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        CreateArchive(uploadPath, docDir,
            valid.SelectMany(r => DocumentReferences.For(r).Select(name => documentPlan.Map(r.CustomerId, name)!)), zipPath);

        return new GeneratedBatch(batchKey, fileName, uploadPath, zipPath, valid.Count, DateTime.UtcNow, skipped, record20Lines);
    }

    /// <summary>Splits the supplied records into those that pass and those that fail validation.</summary>
    private static (List<Individual> Valid, List<SkippedRecord> Skipped) Partition(IReadOnlyList<Individual> records)
    {
        var valid = new List<Individual>();
        var skipped = new List<SkippedRecord>();

        foreach (var r in records)
        {
            var errors = CkycRecordValidator.Validate(r);
            if (errors.Count == 0)
            {
                valid.Add(r);
            }
            else
            {
                skipped.Add(new SkippedRecord(
                    r.CustomerId,
                    $"{r.Name.FirstName} {r.Name.LastName}".Trim(),
                    errors));
            }
        }

        return (valid, skipped);
    }

    private static string FormatValidationFailures(IEnumerable<SkippedRecord> skipped) =>
        string.Join(" ", skipped.Select(s =>
            $"{s.CustomerId}: {string.Join("; ", s.Errors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}"))}"));

    private static (long MasterId, string CustomerId, IReadOnlySet<string> References) Describe(Individual record)
        => (record.MasterRecordId, record.CustomerId, DocumentReferences.For(record));

    private static void ApplyDocumentChecks(List<Individual> valid, List<SkippedRecord> skipped,
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
                var message = unsafeDocument is not null
                    ? "A supporting document must be a PDF, JPG or JPEG file name without a directory path."
                    : $"The following supporting documents have not been imported: {value}.";
                skipped.Add(new SkippedRecord(record.CustomerId, $"{record.Name.FirstName} {record.Name.LastName}".Trim(),
                    [new ValidationError(null, "DOC", null, "Supporting documents", value, null, message)]));
                continue;
            }
            var bytes = documents.Sum(doc => plan.Get(record.MasterRecordId, doc).ByteLength);
            if (bytes <= CkycRecords.MaxIndividualBytesPerCustomer) continue;
            valid.Remove(record);
            skipped.Add(new SkippedRecord(record.CustomerId, $"{record.Name.FirstName} {record.Name.LastName}".Trim(),
                [new ValidationError(null, "DOC", null, "Supporting documents", bytes.ToString(), null,
                    $"Supporting documents total {bytes} bytes; the per-customer limit is {CkycRecords.MaxIndividualBytesPerCustomer} bytes (500 KB).") ]));
        }
    }

    private static bool IsSafeDocumentName(string document) =>
        !Path.IsPathRooted(document)
        && string.Equals(Path.GetFileName(document), document, StringComparison.Ordinal)
        && new[] { ".pdf", ".jpg", ".jpeg" }.Contains(Path.GetExtension(document), StringComparer.OrdinalIgnoreCase);

    private static void CreateArchive(string uploadPath, string docDir, IEnumerable<string> batchDocuments, string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(uploadPath, $"upload/{Path.GetFileName(uploadPath)}", CompressionLevel.Optimal);
        foreach (var document in batchDocuments.Distinct(StringComparer.OrdinalIgnoreCase))
            archive.CreateEntryFromFile(Path.Combine(docDir, document), $"upload/support_docs/{document}", CompressionLevel.Optimal);
    }
}
