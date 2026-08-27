using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using Microsoft.EntityFrameworkCore;
using UpdateBatchEntity = CKYC.Data.Entities.UpdateBatch;
using UpdateRequestEntity = CKYC.Data.Entities.UpdateRequest;
using UpdateResponseEntity = CKYC.Data.Entities.UpdateResponse;
using UpdateResponseFileEntity = CKYC.Data.Entities.UpdateResponseFile;

namespace CKYC.Data;

/// <summary>
/// EF Core (SQL Server) persistence for the bulk-update pipeline: JSON intake rows,
/// per-client-type batch claiming (search_request conventions), FVU audit and
/// .UPD.RESm response import.
/// </summary>
public sealed class UpdateRepository : IUpdateRepository
{
    private readonly ICkycDatabase _db;

    public UpdateRepository(ICkycDatabase db) => _db = db;

    public async Task<UpdateIngestResult> InsertAsync(IReadOnlyList<UpdateRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return new UpdateIngestResult(0, 0);
        await using var db = _db.CreateContext();
        var now = DateTime.UtcNow;
        foreach (var request in requests)
        {
            db.UpdateRequests.Add(new UpdateRequestEntity
            {
                ExternalRequestId = request.ExternalRequestId,
                CustomerId = request.CustomerId,
                ClientType = request.ClientType,
                CkycNumber = request.CkycNumber,
                RawRequestJson = request.RawRequestJson,
                ProcessingStatus = 0,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
        return new UpdateIngestResult(requests.Count, requests.Count);
    }

    public async Task<UpdateClaim?> ClaimAsync(string clientType, int limit, DateOnly businessDate, int sequenceStart,
        TimeSpan claimTimeout, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var token = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(claimTimeout);

        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync($"CKYC:update-claim:{clientType.ToUpperInvariant()}", ct);

        // Serialize the short claim and per-client sequence allocation window across processes.
        var claimIds = await db.UpdateRequests
            .Where(r => r.ClientType == clientType
                     && (r.ProcessingStatus == 0 || (r.ProcessingStatus == 1 && r.ClaimedAt < staleBefore)))
            .OrderBy(r => r.Id).Take(limit)
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (claimIds.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await db.UpdateRequests
            .Where(r => claimIds.Contains(r.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProcessingStatus, 1)
                .SetProperty(r => r.ClaimToken, token)
                .SetProperty(r => r.ClaimedAt, now)
                .SetProperty(r => r.LastError, (string?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);

        // Daily sequence number for the .UPD file name — separate counter per client type.
        var sequence = sequenceStart;
        var maxSequence = await db.UpdateBatches
            .Where(b => b.BusinessDate == businessDate && b.ClientType == clientType)
            .MaxAsync(b => (int?)b.FileSequence, ct);
        if (maxSequence is not null) sequence = Math.Max(sequenceStart, maxSequence.Value + 1);

        var records = await ReadClaimedAsync(db, token, ct);
        db.UpdateBatches.Add(new UpdateBatchEntity
        {
            BusinessDate = businessDate,
            FileSequence = sequence,
            ClientType = clientType,
            ClaimToken = token,
            RecordCount = records.Count,
            Status = 1,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new UpdateClaim(token, businessDate, sequence, clientType, records);
    }

    public async Task CompleteAsync(UpdateClaim claim, string batchKey, string fileName, string filePath,
        IReadOnlyDictionary<string, int> lineByCkycNumber, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var claimIds = claim.Records.Select(record => record.Id).ToList();
        var lineById = claim.Records.ToDictionary(record => record.Id,
            record => lineByCkycNumber.TryGetValue(record.CkycNumber, out var line) ? line : 0);
        var requests = await db.UpdateRequests
            .Where(r => claimIds.Contains(r.Id) && r.ClaimToken == claim.Token && r.ProcessingStatus == 1)
            .ToListAsync(ct);
        foreach (var request in requests)
        {
            request.ProcessingStatus = 2;
            request.ProcessedAt = now;
            request.OutputFileName = fileName;
            request.OutputLineNumber = lineById[request.Id];
            request.OutputBatchKey = batchKey;
            request.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await db.UpdateBatches
            .Where(b => b.ClaimToken == claim.Token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, 2)
                .SetProperty(b => b.FileName, fileName)
                .SetProperty(b => b.FilePath, filePath)
                .SetProperty(b => b.CompletedAt, now), ct);
        await tx.CommitAsync(ct);
    }

    public async Task FailAsync(UpdateClaim claim, string failureMessage, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        await db.UpdateRequests
            .Where(r => r.ClaimToken == claim.Token && r.ProcessingStatus == 1)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProcessingStatus, 3)
                .SetProperty(r => r.LastError, failureMessage)
                .SetProperty(r => r.UpdatedAt, now), ct);
        await db.UpdateBatches
            .Where(b => b.ClaimToken == claim.Token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, 3)
                .SetProperty(b => b.Error, failureMessage)
                .SetProperty(b => b.CompletedAt, now), ct);
        await tx.CommitAsync(ct);
    }

    public async Task SkipAsync(string claimToken, IReadOnlyDictionary<long, string> errorsByRequestId, CancellationToken ct = default)
    {
        if (errorsByRequestId.Count == 0) return;
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var requestIds = errorsByRequestId.Keys.ToList();
        var requests = await db.UpdateRequests
            .Where(r => requestIds.Contains(r.Id) && r.ClaimToken == claimToken && r.ProcessingStatus == 1)
            .ToListAsync(ct);
        foreach (var request in requests)
        {
            request.ProcessingStatus = 3;
            request.LastError = errorsByRequestId[request.Id];
            request.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<UpdateGeneratedBatch?> GetGeneratedBatchAsync(string? fileName, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var query = db.UpdateBatches.AsNoTracking().Where(b => b.Status == 2);
        if (fileName is not null) query = query.Where(b => b.FileName == fileName);
        var batch = await query.OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
        if (batch is null) return null;
        if (string.IsNullOrWhiteSpace(batch.FilePath) || string.IsNullOrWhiteSpace(batch.FileName)) return null;
        return new UpdateGeneratedBatch(batch.Id, batch.FileName, batch.FilePath, batch.RecordCount ?? 0);
    }

    public async Task RecordFvuAsync(long batchId, bool passed, string? zipPath, string? hash, string? failureMessage, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await db.UpdateBatches
            .Where(b => b.Id == batchId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, passed ? 4 : 5)
                .SetProperty(b => b.FvuZipPath, zipPath)
                .SetProperty(b => b.FvuHash, hash)
                .SetProperty(b => b.Error, failureMessage)
                .SetProperty(b => b.CompletedAt, DateTime.UtcNow), ct);
    }

    public async Task<UpdateResponseImportResult> ImportResponseAsync(UpdateResponseImport response, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync($"CKYC:update-response:{response.SourceHash}", ct);

        // SHA-256 duplicate guard so re-running `update-response` never double-imports.
        var duplicate = await db.UpdateResponseFiles.AnyAsync(f => f.SourceHash == response.SourceHash, ct);
        if (duplicate)
        {
            await tx.RollbackAsync(ct);
            return new UpdateResponseImportResult(0, 0, true);
        }

        UpdateBatchEntity? batch = null;
        batch = await db.UpdateBatches
            .Where(b => b.FileName == response.InputFileName)
            .OrderByDescending(b => b.Id)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        db.UpdateResponseFiles.Add(new UpdateResponseFileEntity
        {
            UpdateBatchId = batch?.Id,
            ResponseFileName = response.Header.ResponseFileName,
            ResponseFileNumber = response.Header.ResponseFileNumber,
            ClientType = response.Header.ClientType ?? batch?.ClientType,
            FiCode = response.Header.FiCode,
            RegionCode = response.Header.RegionCode,
            TotalRecords = response.Header.TotalRecords,
            TotalProcessed = response.Header.TotalProcessed,
            RecordsUnderProcessing = response.Header.RecordsUnderProcessing,
            RecordsFailed = response.Header.RecordsFailed,
            ResponseTimestamp = response.Header.ResponseTimestamp,
            Filler1 = response.Header.Filler1,
            Filler2 = response.Header.Filler2,
            RawHeaderData = response.Header.RawHeaderData,
            SourceArchiveName = response.SourceArchiveName,
            SourceHash = response.SourceHash,
            CreatedAt = now,
        });
        var responseLines = response.Details
            .Where(d => d.InputRecord20LineNumber is not null)
            .Select(d => d.InputRecord20LineNumber!.Value).Distinct().ToList();
        var matchedRequests = await db.UpdateRequests
            .Where(r => r.OutputFileName == response.InputFileName
                     && r.OutputLineNumber != null && responseLines.Contains(r.OutputLineNumber.Value))
            .ToListAsync(ct);
        var requestByLine = matchedRequests.GroupBy(r => r.OutputLineNumber!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(r => r.Id).First());

        var matched = 0;
        foreach (var detail in response.Details)
        {
            var request = detail.InputRecord20LineNumber is not null
                && requestByLine.TryGetValue(detail.InputRecord20LineNumber.Value, out var found) ? found : null;
            long? requestId = request?.Id;

            db.UpdateResponses.Add(new UpdateResponseEntity
            {
                UpdateRequestId = requestId,
                ResponseFileName = response.Header.ResponseFileName,
                ResponseFileNumber = response.Header.ResponseFileNumber,
                LineNumber = detail.LineNumber,
                InputRecord20LineNumber = detail.InputRecord20LineNumber,
                AckNumber = detail.AckNumber,
                RecordStatus = detail.RecordStatus,
                CkycNumber = detail.CkycNumber,
                RejectionRemark = detail.RejectionRemark,
                RawResponseData = detail.RawResponseData,
                CreatedAt = now,
            });
            if (request is null) continue;
            matched++;

            // Record status mapping (Update_response sheets): 02 No Match / 03 Rejected.
            var statusName = detail.RecordStatus switch
            {
                "02" => "No Match",
                "03" => "Rejected",
                _ => detail.RecordStatus ?? string.Empty,
            };
            request.ResponseStatus = statusName;
            request.LastAckNumber = detail.AckNumber;
            request.LastResponseStatusCode = detail.RecordStatus;
            request.LastResponseRemark = string.IsNullOrWhiteSpace(detail.RejectionRemark)
                ? detail.CkycNumber : detail.RejectionRemark;
            request.ResponseReadAt = now;
            request.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new UpdateResponseImportResult(response.Details.Count, matched, false);
    }

    /// <summary>Rehydrates claimed rows; RawRequestJson is re-parsed by the processor at write time.</summary>
    private static async Task<List<UpdateRequest>> ReadClaimedAsync(CkycDbContext db, string token, CancellationToken ct)
    {
        var rows = await db.UpdateRequests.AsNoTracking()
            .Where(r => r.ClaimToken == token && r.ProcessingStatus == 1)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        return rows.Select(r => new UpdateRequest
        {
            Id = r.Id,
            ExternalRequestId = r.ExternalRequestId,
            CustomerId = r.CustomerId,
            ClientType = r.ClientType ?? "I",
            CkycNumber = r.CkycNumber ?? string.Empty,
            RawRequestJson = r.RawRequestJson,
            ProcessingStatus = r.ProcessingStatus ?? 0,
            ClaimToken = r.ClaimToken,
            ClaimedAt = r.ClaimedAt,
        }).ToList();
    }
}
