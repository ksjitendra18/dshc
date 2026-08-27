using System.Text.Json;
using CKYC.Core.Abstractions;
using CKYC.Core.Models;
using Microsoft.EntityFrameworkCore;
using BatchEntity = CKYC.Data.Entities.Batch;
using FvuRunEntity = CKYC.Data.Entities.FvuRun;

namespace CKYC.Data;

/// <summary>Persists the generated-batch and FVU-run audit trail (EF Core / SQL Server).</summary>
public sealed class BatchJournal : IBatchJournal
{
    private readonly ICkycDatabase _db;

    public BatchJournal(ICkycDatabase db) => _db = db;

    public async Task LogBatchAsync(GeneratedBatch batch, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        db.Batches.Add(new BatchEntity
        {
            BatchKey = batch.BatchKey,
            UploadFileName = batch.UploadFileName,
            UploadFilePath = batch.UploadFilePath,
            ZipPath = batch.ZipPath,
            RecordCount = batch.RecordCount,
            CreatedAt = batch.CreatedAt,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task LogFvuRunAsync(FvuRunResult result, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        db.FvuRuns.Add(new FvuRunEntity
        {
            BatchKey = result.BatchKey,
            Executed = result.Executed ? 1 : 0,
            ExitCode = result.ExitCode,
            Passed = result.Passed ? 1 : 0,
            SummaryJson = result.Summary is null ? null : JsonSerializer.Serialize(result.Summary),
            OutputZipPath = result.OutputZipPath,
            HashValue = result.Hash,
            ErrorMessage = result.ErrorMessage,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public Task<GeneratedBatch?> GetLastBatchAsync(CancellationToken ct = default)
        => GetBatchAsync((db) => db.Batches.OrderByDescending(b => b.Id).Take(1), null, ct);

    public Task<GeneratedBatch?> GetBatchByKeyAsync(string batchKey, CancellationToken ct = default)
        => GetBatchAsync((db) => db.Batches.Where(b => b.BatchKey == batchKey).OrderByDescending(b => b.Id).Take(1), null, ct);

    public Task<GeneratedBatch?> GetBatchByUploadFileAsync(string uploadFileName, CancellationToken ct = default)
        => GetBatchAsync((db) => db.Batches.Where(b => b.UploadFileName == uploadFileName).OrderByDescending(b => b.Id).Take(1), null, ct);

    private async Task<GeneratedBatch?> GetBatchAsync(Func<CkycDbContext, IQueryable<BatchEntity>> query, string? _, CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var batch = await query(db).AsNoTracking().FirstOrDefaultAsync(ct);
        if (batch is null) return null;
        return new GeneratedBatch(
            batch.BatchKey ?? "",
            batch.UploadFileName ?? "",
            batch.UploadFilePath ?? "",
            batch.ZipPath,
            batch.RecordCount ?? 0,
            batch.CreatedAt ?? DateTime.MinValue);
    }
}
