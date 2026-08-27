using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Spec;
using Microsoft.EntityFrameworkCore;
using FileContentEntity = CKYC.Data.Entities.FileContent;
using IndividualDocumentEntity = CKYC.Data.Entities.IndividualDocument;
using LegalEntityDocumentEntity = CKYC.Data.Entities.LegalEntityDocument;

namespace CKYC.Data;

/// <summary>
/// Shared engine for the source-neutral customer supporting-document store. Content is
/// deduplicated by SHA-256 in <c>file_content</c>; the per-client-type subclasses route
/// the metadata row to <c>individual_document</c> or <c>legal_entity_document</c>.
/// </summary>
public abstract class SqlServerDocumentStoreBase : IDocumentStore
{
    private readonly ICkycDatabase _db;

    protected SqlServerDocumentStoreBase(ICkycDatabase db) => _db = db;

    private ICkycDatabase Db => _db;

    /// <summary>The client type this store serves ("I" or "L") — drives the byte limit.</summary>
    protected abstract string ClientType { get; }

    /// <summary>Total stored byte size for the master record, from the concrete table.</summary>
    protected abstract Task<long> TotalLengthAsync(CkycDbContext db, long masterRecordId, CancellationToken ct);

    /// <summary>Byte size of the existing document with this canonical name, if any.</summary>
    protected abstract Task<long> ExistingLengthAsync(CkycDbContext db, long masterRecordId, string canonicalName, CancellationToken ct);

    /// <summary>Insert-or-update the metadata row in the concrete per-client-type table.</summary>
    protected abstract Task UpsertAsync(CkycDbContext db, long masterRecordId, long contentId,
        string originalName, string canonicalName, string mediaType, DocumentImport import,
        DateTime now, CancellationToken ct);

    /// <summary>Query metadata+content for the given master records, from the concrete table.</summary>
    protected abstract Task<List<DocumentRow>> QueryAsync(CkycDbContext db, IReadOnlyCollection<long> masterRecordIds, CancellationToken ct);

    public async Task<CustomerDocument> ImportAsync(DocumentImport import, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateText(import.SourceType, 30, nameof(import.SourceType), required: true);
        ValidateText(import.SourceReference, 500, nameof(import.SourceReference));
        ValidateText(import.DocumentKind, 50, nameof(import.DocumentKind));

        var originalName = ValidateFileName(import.FileName);
        var canonicalName = Canonicalize(originalName);
        var mediaType = MediaType(originalName);
        var limit = string.Equals(ClientType, "L", StringComparison.OrdinalIgnoreCase)
            ? CkycRecords.MaxLegalEntityBytesPerCustomer
            : CkycRecords.MaxIndividualBytesPerCustomer;
        var bytes = await ReadContentAsync(content, limit, ct);
        ValidateSignature(mediaType, bytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var now = DateTime.UtcNow;

        await using var db = Db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize imports for one customer so the aggregate byte limit cannot be exceeded
        // by two individually-valid concurrent writes. The content lock also makes the
        // hash lookup/insert race-free across different customers.
        await db.AcquireTransactionLockAsync($"CKYC:document-master:{import.MasterRecordId}", ct);
        await db.AcquireTransactionLockAsync($"CKYC:document-content:{sha256}", ct);

        var master = await db.MasterRecords.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == import.MasterRecordId, ct)
            ?? throw new InvalidOperationException($"Master record {import.MasterRecordId} does not exist.");
        var masterClientType = master.ClientType ?? "I";
        if (!string.Equals(masterClientType, ClientType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Master record {import.MasterRecordId} has client type '{masterClientType}'; use the {StoreNameFor(ClientType)} document store.");

        var existingLength = await ExistingLengthAsync(db, import.MasterRecordId, canonicalName, ct);
        var total = await TotalLengthAsync(db, import.MasterRecordId, ct) - existingLength + bytes.LongLength;
        if (total > limit)
            throw new InvalidDataException($"Documents for master record {import.MasterRecordId} total {total} bytes; the limit is {limit} bytes.");

        // Content dedup: the hash-scoped transaction lock closes the lookup/insert race;
        // the unique constraint remains the final integrity backstop.
        var contentId = await db.FileContents
            .Where(f => f.Sha256 == sha256)
            .Select(f => (long?)f.Id)
            .SingleOrDefaultAsync(ct);
        if (contentId is null)
        {
            var row = new FileContentEntity { Sha256 = sha256, Content = bytes, ByteLength = bytes.LongLength, CreatedAt = now };
            db.FileContents.Add(row);
            await db.SaveChangesAsync(ct);
            contentId = row.Id;
        }

        await UpsertAsync(db, import.MasterRecordId, contentId.Value, originalName, canonicalName, mediaType, import, now, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(import.MasterRecordId, originalName, ct)
            ?? throw new DataException("The imported document could not be read back.");
    }

    public async Task<CustomerDocument?> GetAsync(long masterRecordId, string fileName, CancellationToken ct = default)
    {
        var canonical = Canonicalize(fileName);
        await using var db = Db.CreateContext();
        var rows = await QueryAsync(db, [masterRecordId], ct);
        return rows.SingleOrDefault(r => r.CanonicalFileName == canonical)?.ToDocument();
    }

    public async Task<IReadOnlyList<CustomerDocument>> GetByMasterRecordIdsAsync(IReadOnlyCollection<long> masterRecordIds, CancellationToken ct = default)
    {
        if (masterRecordIds.Count == 0) return Array.Empty<CustomerDocument>();
        await using var db = Db.CreateContext();
        var rows = await QueryAsync(db, masterRecordIds, ct);
        return rows.Select(r => r.ToDocument()).ToList();
    }

    protected sealed record DocumentRow(
        long Id, long MasterRecordId, long FileContentId, string OriginalFileName, string CanonicalFileName,
        string MediaType, string? DocumentKind, string SourceType, string? SourceReference,
        string Sha256, long ByteLength, byte[] Content, DateTime CreatedAt, DateTime UpdatedAt)
    {
        public CustomerDocument ToDocument() => new(Id, MasterRecordId, FileContentId, OriginalFileName,
            CanonicalFileName, MediaType, DocumentKind, SourceType, SourceReference, Sha256, ByteLength,
            Content, CreatedAt, UpdatedAt);
    }

    /// <summary>Merge (upsert) semantics shared by both concrete tables.</summary>
    protected static void ApplyUpsertValues(DocumentImport import, out string? documentKind, out string sourceType, out string? sourceReference)
    {
        documentKind = import.DocumentKind;
        sourceType = import.SourceType;
        sourceReference = import.SourceReference;
    }

    private static string StoreNameFor(string clientType) =>
        string.Equals(clientType, "L", StringComparison.OrdinalIgnoreCase) ? "legal-entity" : "individual";

    public static string Canonicalize(string fileName) => fileName.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    private static async Task<byte[]> ReadContentAsync(Stream source, long max, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (memory.Length + read > max) throw new InvalidDataException($"A document cannot exceed {max} bytes.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (memory.Length == 0) throw new InvalidDataException("A document cannot be empty.");
        return memory.ToArray();
    }

    private static string ValidateFileName(string value)
    {
        ValidateText(value, 255, nameof(value), required: true);
        var name = value.Trim();
        if (Path.IsPathRooted(name) || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("A document filename must be a safe basename without a directory path.");
        _ = MediaType(name);
        return name;
    }

    private static string MediaType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => throw new InvalidDataException("Only PDF, JPG and JPEG documents are supported."),
    };

    private static void ValidateSignature(string mediaType, byte[] bytes)
    {
        var valid = mediaType == "application/pdf"
            ? bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
            : bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        if (!valid) throw new InvalidDataException($"The content signature does not match {mediaType}.");
    }

    private static void ValidateText(string? value, int max, string name, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        if (value?.Length > max) throw new ArgumentException($"{name} cannot exceed {max} characters.", name);
    }
}

/// <summary>Document store backed by <c>individual_document</c> (client type I).</summary>
public sealed class IndividualDocumentStore : SqlServerDocumentStoreBase
{
    public IndividualDocumentStore(ICkycDatabase db) : base(db) { }

    protected override string ClientType => "I";

    protected override async Task<long> TotalLengthAsync(CkycDbContext db, long masterRecordId, CancellationToken ct)
        => await db.IndividualDocuments
            .Where(d => d.MasterRecordId == masterRecordId)
            .SumAsync(d => (long?)d.FileContent.ByteLength, ct) ?? 0;

    protected override async Task<long> ExistingLengthAsync(CkycDbContext db, long masterRecordId, string canonicalName, CancellationToken ct)
        => await db.IndividualDocuments
            .Where(d => d.MasterRecordId == masterRecordId && d.CanonicalFileName == canonicalName)
            .Select(d => (long?)d.FileContent.ByteLength)
            .SingleOrDefaultAsync(ct) ?? 0;

    protected override async Task UpsertAsync(CkycDbContext db, long masterRecordId, long contentId,
        string originalName, string canonicalName, string mediaType, DocumentImport import,
        DateTime now, CancellationToken ct)
    {
        var existing = await db.IndividualDocuments
            .SingleOrDefaultAsync(d => d.MasterRecordId == masterRecordId && d.CanonicalFileName == canonicalName, ct);
        if (existing is not null)
        {
            existing.FileContentId = contentId;
            existing.OriginalFileName = originalName;
            existing.MediaType = mediaType;
            existing.DocumentKind = import.DocumentKind;
            existing.SourceType = import.SourceType;
            existing.SourceReference = import.SourceReference;
            existing.UpdatedAt = now;
        }
        else
        {
            db.IndividualDocuments.Add(new IndividualDocumentEntity
            {
                MasterRecordId = masterRecordId,
                FileContentId = contentId,
                OriginalFileName = originalName,
                CanonicalFileName = canonicalName,
                MediaType = mediaType,
                DocumentKind = import.DocumentKind,
                SourceType = import.SourceType,
                SourceReference = import.SourceReference,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    protected override async Task<List<DocumentRow>> QueryAsync(CkycDbContext db, IReadOnlyCollection<long> masterRecordIds, CancellationToken ct)
    {
        var rows = await db.IndividualDocuments.AsNoTracking()
            .Where(d => masterRecordIds.Contains(d.MasterRecordId))
            .OrderBy(d => d.MasterRecordId).ThenBy(d => d.CanonicalFileName)
            .Select(d => new DocumentRow(d.Id, d.MasterRecordId, d.FileContentId, d.OriginalFileName, d.CanonicalFileName,
                d.MediaType, d.DocumentKind, d.SourceType, d.SourceReference, d.FileContent.Sha256, d.FileContent.ByteLength,
                d.FileContent.Content, d.CreatedAt, d.UpdatedAt))
            .ToListAsync(ct);
        return rows;
    }
}

/// <summary>Document store backed by <c>legal_entity_document</c> (client type L).</summary>
public sealed class LegalEntityDocumentStore : SqlServerDocumentStoreBase
{
    public LegalEntityDocumentStore(ICkycDatabase db) : base(db) { }

    protected override string ClientType => "L";

    protected override async Task<long> TotalLengthAsync(CkycDbContext db, long masterRecordId, CancellationToken ct)
        => await db.LegalEntityDocuments
            .Where(d => d.MasterRecordId == masterRecordId)
            .SumAsync(d => (long?)d.FileContent.ByteLength, ct) ?? 0;

    protected override async Task<long> ExistingLengthAsync(CkycDbContext db, long masterRecordId, string canonicalName, CancellationToken ct)
        => await db.LegalEntityDocuments
            .Where(d => d.MasterRecordId == masterRecordId && d.CanonicalFileName == canonicalName)
            .Select(d => (long?)d.FileContent.ByteLength)
            .SingleOrDefaultAsync(ct) ?? 0;

    protected override async Task UpsertAsync(CkycDbContext db, long masterRecordId, long contentId,
        string originalName, string canonicalName, string mediaType, DocumentImport import,
        DateTime now, CancellationToken ct)
    {
        var existing = await db.LegalEntityDocuments
            .SingleOrDefaultAsync(d => d.MasterRecordId == masterRecordId && d.CanonicalFileName == canonicalName, ct);
        if (existing is not null)
        {
            existing.FileContentId = contentId;
            existing.OriginalFileName = originalName;
            existing.MediaType = mediaType;
            existing.DocumentKind = import.DocumentKind;
            existing.SourceType = import.SourceType;
            existing.SourceReference = import.SourceReference;
            existing.UpdatedAt = now;
        }
        else
        {
            db.LegalEntityDocuments.Add(new LegalEntityDocumentEntity
            {
                MasterRecordId = masterRecordId,
                FileContentId = contentId,
                OriginalFileName = originalName,
                CanonicalFileName = canonicalName,
                MediaType = mediaType,
                DocumentKind = import.DocumentKind,
                SourceType = import.SourceType,
                SourceReference = import.SourceReference,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    protected override async Task<List<DocumentRow>> QueryAsync(CkycDbContext db, IReadOnlyCollection<long> masterRecordIds, CancellationToken ct)
    {
        var rows = await db.LegalEntityDocuments.AsNoTracking()
            .Where(d => masterRecordIds.Contains(d.MasterRecordId))
            .OrderBy(d => d.MasterRecordId).ThenBy(d => d.CanonicalFileName)
            .Select(d => new DocumentRow(d.Id, d.MasterRecordId, d.FileContentId, d.OriginalFileName, d.CanonicalFileName,
                d.MediaType, d.DocumentKind, d.SourceType, d.SourceReference, d.FileContent.Sha256, d.FileContent.ByteLength,
                d.FileContent.Content, d.CreatedAt, d.UpdatedAt))
            .ToListAsync(ct);
        return rows;
    }
}
