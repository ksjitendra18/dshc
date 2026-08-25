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

    public CkycLegalEntityBatchGenerator(BatchSettings batch, IFileHasher hasher)
    {
        _batch = batch;
        _hasher = hasher;
    }

    public Task<GeneratedBatch> GenerateAsync(IReadOnlyList<LegalEntity> records, DateOnly businessDate, CancellationToken ct = default)
    {
        if (records.Count == 0)
            throw new InvalidOperationException("No legal-entity records supplied to the batch generator.");
        if (records.Count > CkycRecords.MaxLegalEntityBatchRecords)
            throw new InvalidOperationException($"A legal-entity batch cannot contain more than {CkycRecords.MaxLegalEntityBatchRecords} customers.");

        var (valid, skipped) = Partition(records);
        if (valid.Count == 0)
            throw new InvalidOperationException(
                $"All {records.Count} legal-entity record(s) failed validation — no batch was produced. " +
                FormatValidationFailures(skipped));

        var fileName = CkycFileName.Build("L", _batch.UserId, _batch.FiCode, businessDate, _batch.SequenceStart, "UPL");
        var batchKey = Path.GetFileNameWithoutExtension(fileName);

        var batchDir = Path.Combine(_batch.OutputRoot, batchKey);
        var uploadDir = Path.Combine(batchDir, "upload");
        var docDir = Path.Combine(uploadDir, "support_docs");
        Directory.CreateDirectory(uploadDir);
        Directory.CreateDirectory(docDir);

        ApplyCustomerSizeLimits(valid, skipped, docDir);
        if (valid.Count == 0)
            throw new InvalidOperationException($"All {records.Count} legal-entity record(s) failed validation or document-size limits. " +
                FormatValidationFailures(skipped));

        var writer = new CkycLegalEntityUploadWriter(_batch);
        var content = writer.Write(valid, businessDate);
        var record20Lines = CkycLegalEntityUploadWriter.ComputeRecord20Lines(valid);

        var uploadPath = Path.Combine(uploadDir, fileName);
        File.WriteAllText(uploadPath, content, new UTF8Encoding(false));

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

    private static (List<LegalEntity> Valid, List<SkippedRecord> Skipped) Partition(IReadOnlyList<LegalEntity> records)
    {
        var valid = new List<LegalEntity>();
        var skipped = new List<SkippedRecord>();

        foreach (var r in records)
        {
            var errors = LegalEntityRecordValidator.Validate(r);
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

    private static HashSet<string> EnumerateDocs(LegalEntity le)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Maybe(set, le.PanDocument);
        Maybe(set, le.TinGstnDocument);
        Maybe(set, le.RegisteredAddressDocument);
        Maybe(set, le.PrincipalAddressDocument);
        foreach (var p in le.Proofs)
        {
            Maybe(set, p.CertificateOfIncorporation);
            Maybe(set, p.MemorandumAndArticles);
            Maybe(set, p.ResolutionBoardPoA);
            Maybe(set, p.NamesSeniorManagement);
            Maybe(set, p.CertificateOfCommencement);
            Maybe(set, p.OthersCompany);
            Maybe(set, p.RegistrationCertificate);
            Maybe(set, p.LlpinCertificate);
            Maybe(set, p.PartnershipDeed);
            Maybe(set, p.NamesAllPartners);
            Maybe(set, p.OthersPartnership);
            Maybe(set, p.TrustRegistrationCertificate);
            Maybe(set, p.TrustDeed);
            Maybe(set, p.NamesBeneficiariesTrustees);
            Maybe(set, p.TrustPowerOfAttorney);
            Maybe(set, p.OthersTrust);
            Maybe(set, p.UnincorporatedRegistrationCertificate);
            Maybe(set, p.ResolutionManagingBody);
            Maybe(set, p.UnincorporatedPowerOfAttorney);
            Maybe(set, p.InfoEstablishExistence);
            Maybe(set, p.OthersUnincorporated);
            Maybe(set, p.SupportingDocumentsPoi);
            Maybe(set, p.OtherTypeRegistrationCertificate);
            Maybe(set, p.OtherTypePowerOfAttorney);
            Maybe(set, p.ActivityProof1);
            Maybe(set, p.ActivityProof2);
            Maybe(set, p.OthersOtherType);
        }
        Maybe(set, le.Other?.DeclarationDocument);
        Maybe(set, le.Other?.ConsentDocument);
        return set;
    }

    private static string FormatValidationFailures(IEnumerable<SkippedRecord> skipped) =>
        string.Join(" ", skipped.Select(s =>
            $"{s.CustomerId}: {string.Join("; ", s.Errors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}"))}"));

    private static void Maybe(HashSet<string> set, string? doc)
    {
        if (!string.IsNullOrWhiteSpace(doc)) set.Add(doc);
    }

    private static void ApplyCustomerSizeLimits(List<LegalEntity> valid, List<SkippedRecord> skipped, string docDir)
    {
        foreach (var record in valid.ToList())
        {
            var documents = EnumerateDocs(record);
            var unsafeDocument = documents.FirstOrDefault(doc => !IsSafeDocumentName(doc));
            if (unsafeDocument is not null)
            {
                valid.Remove(record);
                skipped.Add(new SkippedRecord(record.CustomerId, record.EntityName,
                    [new ValidationError(null, "DOC", null, "Supporting documents", unsafeDocument, null,
                        "A supporting document must be a PDF, JPG or JPEG file name without a directory path.")]));
                continue;
            }
            var total = documents.Sum(doc => ExistingOrPlaceholderLength(docDir, doc));
            var oversizedSmallDocument = SmallDocuments(record)
                .FirstOrDefault(doc => ExistingOrPlaceholderLength(docDir, doc) > CkycRecords.MaxLegalSmallDocumentBytes);
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
