using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Spec;
using Microsoft.Data.Sqlite;

namespace CKYC.Data;

/// <summary>SQLite BLOB store for source-neutral customer supporting documents.</summary>
public sealed class SqliteDocumentStore : IDocumentStore
{
    private readonly ICkycDatabase _db;

    public SqliteDocumentStore(ICkycDatabase db) => _db = db;

    public async Task<CustomerDocument> ImportAsync(DocumentImport import, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateText(import.SourceType, 30, nameof(import.SourceType), required: true);
        ValidateText(import.SourceReference, 500, nameof(import.SourceReference));
        ValidateText(import.DocumentKind, 50, nameof(import.DocumentKind));

        var originalName = ValidateFileName(import.FileName);
        var canonicalName = Canonicalize(originalName);
        var mediaType = MediaType(originalName);
        var bytes = await ReadContentAsync(content, CkycRecords.MaxLegalEntityBytesPerCustomer, ct);
        ValidateSignature(mediaType, bytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = (SqliteConnection)_db.Create();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        var clientType = await GetClientTypeAsync(connection, transaction, import.MasterRecordId, ct)
            ?? throw new InvalidOperationException($"Master record {import.MasterRecordId} does not exist.");
        var limit = string.Equals(clientType, "L", StringComparison.OrdinalIgnoreCase)
            ? CkycRecords.MaxLegalEntityBytesPerCustomer
            : CkycRecords.MaxIndividualBytesPerCustomer;
        var existingLength = await GetExistingLengthAsync(connection, transaction, import.MasterRecordId, canonicalName, ct);
        var total = await GetTotalLengthAsync(connection, transaction, import.MasterRecordId, ct) - existingLength + bytes.LongLength;
        if (total > limit)
            throw new InvalidDataException($"Documents for master record {import.MasterRecordId} total {total} bytes; the limit is {limit} bytes.");

        await using (var insertContent = connection.CreateCommand())
        {
            insertContent.Transaction = transaction;
            insertContent.CommandText = """
                INSERT OR IGNORE INTO file_content (Sha256, Content, ByteLength, CreatedAt)
                VALUES (@hash, @content, @length, @created)
                """;
            insertContent.Parameters.AddWithValue("@hash", sha256);
            insertContent.Parameters.Add("@content", SqliteType.Blob).Value = bytes;
            insertContent.Parameters.AddWithValue("@length", bytes.LongLength);
            insertContent.Parameters.AddWithValue("@created", now);
            await insertContent.ExecuteNonQueryAsync(ct);
        }

        long contentId;
        await using (var findContent = connection.CreateCommand())
        {
            findContent.Transaction = transaction;
            findContent.CommandText = "SELECT Id FROM file_content WHERE Sha256=@hash";
            findContent.Parameters.AddWithValue("@hash", sha256);
            contentId = Convert.ToInt64(await findContent.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO customer_document
                    (MasterRecordId, FileContentId, OriginalFileName, CanonicalFileName, MediaType,
                     DocumentKind, SourceType, SourceReference, CreatedAt, UpdatedAt)
                VALUES (@master, @content, @original, @canonical, @media, @kind, @source, @reference, @now, @now)
                ON CONFLICT(MasterRecordId, CanonicalFileName) DO UPDATE SET
                    FileContentId=excluded.FileContentId,
                    OriginalFileName=excluded.OriginalFileName,
                    MediaType=excluded.MediaType,
                    DocumentKind=excluded.DocumentKind,
                    SourceType=excluded.SourceType,
                    SourceReference=excluded.SourceReference,
                    UpdatedAt=excluded.UpdatedAt
                """;
            upsert.Parameters.AddWithValue("@master", import.MasterRecordId);
            upsert.Parameters.AddWithValue("@content", contentId);
            upsert.Parameters.AddWithValue("@original", originalName);
            upsert.Parameters.AddWithValue("@canonical", canonicalName);
            upsert.Parameters.AddWithValue("@media", mediaType);
            upsert.Parameters.AddWithValue("@kind", (object?)import.DocumentKind ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@source", import.SourceType);
            upsert.Parameters.AddWithValue("@reference", (object?)import.SourceReference ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@now", now);
            await upsert.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return await GetAsync(import.MasterRecordId, originalName, ct)
            ?? throw new DataException("The imported document could not be read back.");
    }

    public async Task<CustomerDocument?> GetAsync(long masterRecordId, string fileName, CancellationToken ct = default)
    {
        var canonical = Canonicalize(fileName);
        await using var connection = (SqliteConnection)_db.Create();
        await using var command = SelectCommand(connection);
        command.CommandText += " WHERE d.MasterRecordId=@master AND d.CanonicalFileName=@name";
        command.Parameters.AddWithValue("@master", masterRecordId);
        command.Parameters.AddWithValue("@name", canonical);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CustomerDocument>> GetByMasterRecordIdsAsync(IReadOnlyCollection<long> masterRecordIds, CancellationToken ct = default)
    {
        if (masterRecordIds.Count == 0) return Array.Empty<CustomerDocument>();
        await using var connection = (SqliteConnection)_db.Create();
        await using var command = SelectCommand(connection);
        var names = masterRecordIds.Select((_, i) => $"@id{i}").ToArray();
        command.CommandText += $" WHERE d.MasterRecordId IN ({string.Join(',', names)}) ORDER BY d.MasterRecordId,d.CanonicalFileName";
        var i = 0;
        foreach (var id in masterRecordIds) command.Parameters.AddWithValue(names[i++], id);
        var result = new List<CustomerDocument>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    public static string Canonicalize(string fileName) => fileName.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    private static SqliteCommand SelectCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Id,d.MasterRecordId,d.FileContentId,d.OriginalFileName,d.CanonicalFileName,
                   d.MediaType,d.DocumentKind,d.SourceType,d.SourceReference,c.Sha256,c.ByteLength,
                   c.Content,d.CreatedAt,d.UpdatedAt
              FROM customer_document d JOIN file_content c ON c.Id=d.FileContentId
            """;
        return command;
    }

    private static CustomerDocument Read(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
        r.GetString(9), r.GetInt64(10), (byte[])r[11], DateTime.Parse(r.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTime.Parse(r.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static async Task<string?> GetClientTypeAsync(SqliteConnection c, SqliteTransaction tx, long id, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(ClientType,'I') FROM master_record WHERE Id=@id"; cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<long> GetTotalLengthAsync(SqliteConnection c, SqliteTransaction tx, long id, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(SUM(f.ByteLength),0) FROM customer_document d JOIN file_content f ON f.Id=d.FileContentId WHERE d.MasterRecordId=@id";
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<long> GetExistingLengthAsync(SqliteConnection c, SqliteTransaction tx, long id, string name, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(f.ByteLength,0) FROM customer_document d JOIN file_content f ON f.Id=d.FileContentId WHERE d.MasterRecordId=@id AND d.CanonicalFileName=@name";
        cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@name", name);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

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
