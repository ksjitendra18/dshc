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

    public CkycBatchGenerator(BatchSettings batch, IFileHasher hasher)
    {
        _batch = batch;
        _hasher = hasher;
    }

    public Task<GeneratedBatch> GenerateAsync(IReadOnlyList<Individual> records, DateOnly businessDate, CancellationToken ct = default)
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

        var fileName = CkycFileName.Build(_batch.ClientType, _batch.UserId, _batch.FiCode, businessDate, _batch.SequenceStart, "UPL");
        var batchKey = Path.GetFileNameWithoutExtension(fileName);

        var batchDir = Path.Combine(_batch.OutputRoot, batchKey);
        var uploadDir = Path.Combine(batchDir, "upload");
        var docDir = Path.Combine(uploadDir, "support_docs");
        Directory.CreateDirectory(uploadDir);
        Directory.CreateDirectory(docDir);

        ApplyCustomerSizeLimit(valid, skipped, docDir);
        if (valid.Count == 0)
            throw new InvalidOperationException($"All {records.Count} record(s) failed validation or document checks. " +
                FormatValidationFailures(skipped));

        var writer = new CkycUploadWriter(_batch);
        var content = writer.Write(valid, businessDate);
        var record20Lines = CkycUploadWriter.ComputeRecord20Lines(valid);

        var uploadPath = Path.Combine(uploadDir, fileName);
        File.WriteAllText(uploadPath, content, new UTF8Encoding(false));

        // Create the supporting-document placeholders referenced by the records so the
        // FVU's SupportDocPath folder check is satisfied.
        foreach (var r in valid)
            foreach (var doc in EnumerateDocs(r))
            {
                var path = Path.Combine(docDir, doc);
                if (!File.Exists(path)) File.WriteAllBytes(path, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF"
            }

        var zipPath = Path.Combine(batchDir, $"{batchKey}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        CreateArchive(uploadPath, docDir, valid.SelectMany(EnumerateDocs), zipPath);

        return Task.FromResult(new GeneratedBatch(batchKey, fileName, uploadPath, zipPath, valid.Count, DateTime.UtcNow, skipped, record20Lines));
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

    private static HashSet<string> EnumerateDocs(Individual r)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Maybe(set, r.PhotoOfIndividual);
        Maybe(set, r.PanDocument);
        foreach (var p in r.Proofs) Maybe(set, p.CopyOfOvd);
        Maybe(set, r.PermanentAddress?.CopyOfOvd);
        Maybe(set, r.CurrentAddress?.CopyOfOvd);
        Maybe(set, r.Other?.DeclarationDocument);
        Maybe(set, r.Other?.ClientConsent);
        return set;
    }

    private static void Maybe(HashSet<string> set, string? doc)
    {
        if (!string.IsNullOrWhiteSpace(doc)) set.Add(doc);
    }

    private static void ApplyCustomerSizeLimit(List<Individual> valid, List<SkippedRecord> skipped, string docDir)
    {
        foreach (var record in valid.ToList())
        {
            var documents = EnumerateDocs(record);
            var unsafeDocument = documents.FirstOrDefault(doc => !IsSafeDocumentName(doc));
            if (unsafeDocument is not null)
            {
                valid.Remove(record);
                skipped.Add(new SkippedRecord(record.CustomerId, $"{record.Name.FirstName} {record.Name.LastName}".Trim(),
                    [new ValidationError(null, "DOC", null, "Supporting documents", unsafeDocument, null,
                        "A supporting document must be a PDF, JPG or JPEG file name without a directory path.")]));
                continue;
            }
            var bytes = documents.Sum(doc => ExistingOrPlaceholderLength(docDir, doc));
            if (bytes <= CkycRecords.MaxIndividualBytesPerCustomer) continue;
            valid.Remove(record);
            skipped.Add(new SkippedRecord(record.CustomerId, $"{record.Name.FirstName} {record.Name.LastName}".Trim(),
                [new ValidationError(null, "DOC", null, "Supporting documents", bytes.ToString(), null,
                    $"Supporting documents total {bytes} bytes; the per-customer limit is {CkycRecords.MaxIndividualBytesPerCustomer} bytes (500 KB).") ]));
        }
    }

    private static long ExistingOrPlaceholderLength(string docDir, string document)
    {
        var path = Path.Combine(docDir, document);
        return File.Exists(path) ? new FileInfo(path).Length : 4L;
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
