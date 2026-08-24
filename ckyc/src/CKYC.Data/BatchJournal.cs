using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Models;
using static CKYC.Data.MasterRepository;

namespace CKYC.Data;

/// <summary>Persists the generated-batch and FVU-run audit trail.</summary>
public sealed class BatchJournal : IBatchJournal
{
    private readonly ICkycDatabase _db;

    public BatchJournal(ICkycDatabase db) => _db = db;

    public async Task LogBatchAsync(GeneratedBatch batch, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO batch (BatchKey, UploadFileName, UploadFilePath, ZipPath, RecordCount, CreatedAt)
            VALUES (@key, @file, @path, @zip, @count, @now)
            """;
        cmd.Parameters.Add(NewParam("@key", batch.BatchKey));
        cmd.Parameters.Add(NewParam("@file", batch.UploadFileName));
        cmd.Parameters.Add(NewParam("@path", batch.UploadFilePath));
        cmd.Parameters.Add(NewParam("@zip", batch.ZipPath));
        cmd.Parameters.Add(NewParam("@count", batch.RecordCount));
        cmd.Parameters.Add(NewParam("@now", batch.CreatedAt.ToString("o")));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task LogFvuRunAsync(FvuRunResult result, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO fvu_run (BatchKey, Executed, ExitCode, Passed, SummaryJson, OutputZipPath, HashValue, ErrorMessage, CreatedAt)
            VALUES (@key, @exec, @exit, @passed, @summary, @zip, @hash, @err, @now)
            """;
        cmd.Parameters.Add(NewParam("@key", result.BatchKey));
        cmd.Parameters.Add(NewParam("@exec", result.Executed ? 1 : 0));
        cmd.Parameters.Add(NewParam("@exit", result.ExitCode));
        cmd.Parameters.Add(NewParam("@passed", result.Passed ? 1 : 0));
        cmd.Parameters.Add(NewParam("@summary", result.Summary is null ? null : System.Text.Json.JsonSerializer.Serialize(result.Summary)));
        cmd.Parameters.Add(NewParam("@zip", result.OutputZipPath));
        cmd.Parameters.Add(NewParam("@hash", result.Hash));
        cmd.Parameters.Add(NewParam("@err", result.ErrorMessage));
        cmd.Parameters.Add(NewParam("@now", DateTime.UtcNow.ToString("o")));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<GeneratedBatch?> GetLastBatchAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM batch ORDER BY Id DESC LIMIT 1";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new GeneratedBatch(
            r["BatchKey"] as string ?? "",
            r["UploadFileName"] as string ?? "",
            r["UploadFilePath"] as string ?? "",
            r["ZipPath"] as string,
            Convert.ToInt32(r["RecordCount"]),
            r["CreatedAt"] is string s && DateTime.TryParse(s, out var d) ? d : DateTime.MinValue);
    }
}
