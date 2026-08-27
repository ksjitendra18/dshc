using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using Microsoft.EntityFrameworkCore;
using ActivityTypeEntity = CKYC.Data.Entities.ActivityType;
using MasterRecordAttemptEntity = CKYC.Data.Entities.MasterRecordAttempt;
using MasterRecordBatchEntity = CKYC.Data.Entities.MasterRecordBatch;
using MasterRecordEntity = CKYC.Data.Entities.MasterRecord;
using MasterRecordReattemptEntity = CKYC.Data.Entities.MasterRecordReattempt;
using MasterRecordResponseEntity = CKYC.Data.Entities.MasterRecordResponse;
using StatusMasterEntity = CKYC.Data.Entities.StatusMaster;
using UploadResponseFileEntity = CKYC.Data.Entities.UploadResponseFile;

namespace CKYC.Data;

/// <summary>
/// EF Core (SQL Server) implementation of the master table operations: daily fetch
/// upsert, stage transitions, retry bookkeeping, CERSAI response mirroring and the
/// master lookups. Every status write also maintains the denormalized StatusCode
/// column (PND/CRM/SAV/BAT/…) in the same statement so the two never drift.
/// </summary>
public sealed class MasterRepository : IMasterRepository
{
    private readonly ICkycDatabase _db;

    public MasterRepository(ICkycDatabase db) => _db = db;

    public async Task<FetchResult> UpsertDailyAsync(IReadOnlyCollection<string> customerIds, DateOnly businessDate, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return new FetchResult(0, 0, 0);

        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync("CKYC:master-record-upsert", ct);
        var existing = await db.MasterRecords
            .Where(m => customerIds.Contains(m.CustomerId!))
            .Select(m => m.CustomerId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        foreach (var id in customerIds)
        {
            // Add returns false for both a database hit and an earlier duplicate in this
            // input collection, preserving the SQLite INSERT..WHERE NOT EXISTS behavior.
            if (!existingSet.Add(id)) continue;
            db.MasterRecords.Add(new MasterRecordEntity
            {
                CustomerId = id,
                BusinessDate = businessDate,
                Status = (int)MasterRecordStatus.Pending,
                StatusCode = MasterRecordStatusCode.For(MasterRecordStatus.Pending),
                Remarks = $"Fetched on {businessDate:dd-MM-yyyy}",
                RetryCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
            });
            inserted++;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new FetchResult(inserted, customerIds.Count - inserted, customerIds.Count);
    }

    public async Task<IReadOnlyList<MasterRecord>> GetByStatusAsync(MasterRecordStatus status, int limit, string? clientType = null, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var query = db.MasterRecords.AsNoTracking()
            .Where(m => m.Status == (int)status);
        if (!string.IsNullOrWhiteSpace(clientType))
            query = query.Where(m => m.ClientType == clientType);
        var rows = await query.OrderBy(m => m.Id).Take(limit).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<MasterRecord>> GetRetryableAsync(int maxRetries, int limit, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var rows = await db.MasterRecords.AsNoTracking()
            .Where(m => m.Status == (int)MasterRecordStatus.Failed && m.RetryCount < maxRetries)
            .OrderBy(m => m.Id).Take(limit).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<MasterRecord>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return Array.Empty<MasterRecord>();
        await using var db = _db.CreateContext();
        var rows = await db.MasterRecords.AsNoTracking()
            .Where(m => customerIds.Contains(m.CustomerId!))
            .OrderBy(m => m.Id).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<MasterRecord?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var row = await db.MasterRecords.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<MasterRecord> EnsureAsync(string customerId, DateOnly businessDate, string? clientType = null, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync("CKYC:master-record-upsert", ct);
        var existing = await db.MasterRecords.FirstOrDefaultAsync(m => m.CustomerId == customerId, ct);
        if (existing is not null)
        {
            await tx.CommitAsync(ct);
            return ToDomain(existing);
        }

        var now = DateTime.UtcNow;
        var row = new MasterRecordEntity
        {
            CustomerId = customerId,
            ClientType = string.IsNullOrWhiteSpace(clientType) ? "I" : clientType,
            BusinessDate = businessDate,
            Status = (int)MasterRecordStatus.Pending,
            StatusCode = MasterRecordStatusCode.For(MasterRecordStatus.Pending),
            Remarks = $"Inserted on {businessDate:dd-MM-yyyy}",
            RetryCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.MasterRecords.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDomain(row);
    }

    public async Task<IReadOnlyList<MasterRecord>> GetByBatchFileAsync(string batchFile, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var rows = await db.MasterRecords.AsNoTracking()
            .Where(m => db.MasterRecordBatches.Any(b => b.MasterRecordId == m.Id && b.BatchFile == batchFile))
            .OrderBy(m => m.Id).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<MasterRecord?> GetByBatchLineAsync(string batchFile, int record20Line, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var row = await db.MasterRecordBatches.AsNoTracking()
            .Where(b => b.BatchFile == batchFile && b.Record20LineNumber == record20Line)
            .OrderByDescending(b => b.Id)
            .Join(db.MasterRecords.AsNoTracking(), b => b.MasterRecordId, m => m.Id, (b, m) => m)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<CustomerBatchRecord>> GetBatchHistoryAsync(string customerId, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var rows = await db.MasterRecordBatches.AsNoTracking()
            .Where(b => b.CustomerId == customerId)
            .OrderBy(b => b.BatchedAt).ThenBy(b => b.Id)
            .Select(b => new CustomerBatchRecord
            {
                Id = b.Id,
                MasterRecordId = b.MasterRecordId ?? 0,
                CustomerId = b.CustomerId ?? string.Empty,
                BatchFile = b.BatchFile ?? string.Empty,
                Record20LineNumber = b.Record20LineNumber,
                BatchedAt = b.BatchedAt ?? DateTime.MinValue,
            })
            .ToListAsync(ct);
        return rows;
    }

    public async Task<bool> UpdateStatusAsync(long id, MasterRecordStatus status, string? remarks, string? lastError, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        var row = await db.MasterRecords.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return false;

        row.Status = (int)status;
        row.StatusCode = MasterRecordStatusCode.For(status);
        row.Remarks = remarks;
        row.LastError = lastError;
        row.LastAttemptAt = now;
        row.UpdatedAt = now;

        var (flag, timestamp) = StageFor(status);
        switch (flag)
        {
            case "IsCrmFetched": row.IsCrmFetched = 1; break;
            case "IsSaved": row.IsSaved = 1; break;
            case "IsBatched": row.IsBatched = 1; break;
            case "IsUploaded": row.IsUploaded = 1; break;
            case "IsResponseRead": row.IsResponseRead = 1; break;
            case "IsReconciled": row.IsReconciled = 1; break;
            case "IsRejected": row.IsRejected = 1; break;
        }
        switch (timestamp)
        {
            case "CrmFetchedAt": row.CrmFetchedAt ??= now; break;
            case "SavedAt": row.SavedAt ??= now; break;
            case "BatchedAt": row.BatchedAt ??= now; break;
            case "UploadedAt": row.UploadedAt ??= now; break;
            case "FirstResponseAt": row.FirstResponseAt ??= now; break;
            case "ReconciledAt": row.ReconciledAt ??= now; break;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IncrementRetryAsync(long id, string? lastError, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        return await db.MasterRecords
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.LastError, lastError)
                .SetProperty(m => m.LastAttemptAt, now)
                .SetProperty(m => m.UpdatedAt, now), ct) > 0;
    }

    public async Task<bool> RecordRetryAsync(long id, int retryCount, string? lastError, string? lastActivity,
        DateTime? nextRetryAt, bool needsReconcile, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        return await db.MasterRecords
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.RetryCount, retryCount)
                .SetProperty(m => m.LastError, lastError)
                .SetProperty(m => m.LastActivity, lastActivity)
                .SetProperty(m => m.LastAttemptAt, now)
                .SetProperty(m => m.NextRetryAt, nextRetryAt)
                .SetProperty(m => m.NeedsReconcile, needsReconcile ? 1 : 0)
                .SetProperty(m => m.UpdatedAt, now), ct) > 0;
    }

    public async Task<bool> MarkNeedsReconcileAsync(long id, string reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        return await db.MasterRecords
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.NeedsReconcile, 1)
                .SetProperty(m => m.ReconStatus, "NeedsIntervention")
                .SetProperty(m => m.ReconRemarks, reason)
                .SetProperty(m => m.UpdatedAt, now), ct) > 0;
    }

    public async Task<bool> ClearRetryStateAsync(long id, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        return await db.MasterRecords
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.RetryCount, 0)
                .SetProperty(m => m.LastError, (string?)null)
                .SetProperty(m => m.LastActivity, (string?)null)
                .SetProperty(m => m.NextRetryAt, (DateTime?)null)
                .SetProperty(m => m.NeedsReconcile, 0)
                .SetProperty(m => m.UpdatedAt, now), ct) > 0;
    }

    public async Task<IReadOnlyList<MasterRecord>> GetRetryableForActivityAsync(string activityCode, int maxAttempts,
        DateTime now, int limit, CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var rows = await db.MasterRecords.AsNoTracking()
            .Where(m => m.Status == (int)MasterRecordStatus.Failed
                     && m.RetryCount < maxAttempts
                     && m.LastActivity == activityCode
                     && (m.NextRetryAt == null || m.NextRetryAt <= now))
            .OrderBy(m => m.Id).Take(limit).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<MasterRecord>> GetNeedsReconcileAsync(string? kind, int limit, CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var failed = (int)MasterRecordStatus.Failed;
        var rejected = (int)MasterRecordStatus.Rejected;
        var fvuFailed = (int)MasterRecordStatus.FvuFailed;

        var query = kind switch
        {
            "retry" => db.MasterRecords.AsNoTracking()
                .Where(m => m.NeedsReconcile == 1 && (m.Status == failed || m.Status == rejected)),
            "cersai" => db.MasterRecords.AsNoTracking()
                .Where(m => m.Status == rejected || m.IsRejected == 1 || m.Status == fvuFailed),
            _ => db.MasterRecords.AsNoTracking()
                .Where(m => m.NeedsReconcile == 1 || m.Status == rejected || m.IsRejected == 1 || m.Status == fvuFailed),
        };

        var rows = await query.OrderBy(m => m.Id).Take(limit).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<int> MarkBatchAsync(IReadOnlyCollection<long> ids, string batchFile,
        IReadOnlyDictionary<long, int>? lineByRecord, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var rows = await db.MasterRecords.Where(m => ids.Contains(m.Id)).ToListAsync(ct);
        var rowIds = rows.Select(row => row.Id).ToList();
        await db.MasterRecordBatches
            .Where(b => rowIds.Contains(b.MasterRecordId ?? 0) && b.BatchFile == batchFile)
            .ExecuteDeleteAsync(ct);
        foreach (var row in rows)
        {
            row.Status = (int)MasterRecordStatus.Batched;
            row.StatusCode = MasterRecordStatusCode.For(MasterRecordStatus.Batched);
            row.BatchFile = batchFile;
            row.IsBatched = 1;
            row.BatchedAt ??= now;
            row.UpdatedAt = now;
            if (lineByRecord is not null && lineByRecord.TryGetValue(row.Id, out var line))
                row.BatchRecordLine = line;

            db.MasterRecordBatches.Add(new MasterRecordBatchEntity
            {
                MasterRecordId = row.Id,
                CustomerId = row.CustomerId,
                BatchFile = batchFile,
                Record20LineNumber = row.BatchRecordLine,
                BatchedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Count;
    }

    public async Task<int> CountByStatusAsync(MasterRecordStatus status, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        return await db.MasterRecords.CountAsync(m => m.Status == (int)status, ct);
    }

    public async Task<MasterRecordResponse> AddResponseAsync(MasterRecordResponse response, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var readAt = response.ReadAt ?? now;

        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync($"CKYC:master-response:{response.MasterRecordId}", ct);

        // Idempotent re-read: replace any prior row for this (record, response file, line).
        await db.MasterRecordResponses
            .Where(r => r.MasterRecordId == response.MasterRecordId
                     && r.ResponseFileName == response.ResponseFileName
                     && r.LineNumber == response.LineNumber)
            .ExecuteDeleteAsync(ct);

        db.MasterRecordResponses.Add(new MasterRecordResponseEntity
        {
            MasterRecordId = response.MasterRecordId,
            CustomerId = response.CustomerId,
            BatchFile = response.BatchFile,
            ResponseFileNumber = response.ResponseFileNumber,
            ResponseFileName = response.ResponseFileName,
            LineNumber = response.LineNumber,
            InputRecordLineNumber = response.InputRecordLineNumber,
            AckNumber = response.AckNumber,
            RecordStatus = response.RecordStatus,
            CkycReferenceNumber = response.CkycReferenceNumber,
            CkycNumber = response.CkycNumber,
            RejectionRemark = response.RejectionRemark,
            ReadAt = readAt,
            Remarks = response.Remarks,
            RawData = response.RawData,
            CreatedAt = now,
        });
        // Mirror the latest reply onto the master summary, but never regress to an
        // older response file number.
        var master = await db.MasterRecords
            .Where(m => m.Id == response.MasterRecordId && m.BatchFile == response.BatchFile
                     && (m.LastResponseFileNumber == null || response.ResponseFileNumber >= m.LastResponseFileNumber))
            .SingleOrDefaultAsync(ct);
        if (master is not null)
        {
            master.Status = (int)MasterRecordStatus.ResponseRead;
            master.StatusCode = MasterRecordStatusCode.For(MasterRecordStatus.ResponseRead);
            master.IsResponseRead = 1;
            master.FirstResponseAt ??= now;
            master.LastResponseFileNumber = response.ResponseFileNumber;
            master.LastResponseFileName = response.ResponseFileName;
            master.LastResponseAckNumber = response.AckNumber;
            master.LastResponseStatus = response.RecordStatus;
            master.LastResponseCkycReference = response.CkycReferenceNumber;
            master.LastResponseCkycNumber = response.CkycNumber;
            master.LastResponseRejectionRemark = response.RejectionRemark;
            master.LastResponseReadAt = readAt;
            master.LastResponseRemarks = response.Remarks;
            master.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        response.CreatedAt = now;
        return response;
    }

    public async Task<IReadOnlyList<MasterRecordResponse>> GetResponsesAsync(long masterRecordId, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var rows = await db.MasterRecordResponses.AsNoTracking()
            .Where(r => r.MasterRecordId == masterRecordId)
            .OrderBy(r => r.ResponseFileNumber).ThenBy(r => r.LineNumber)
            .ToListAsync(ct);
        return rows.Select(r => new MasterRecordResponse
        {
            Id = r.Id,
            MasterRecordId = r.MasterRecordId ?? 0,
            CustomerId = r.CustomerId ?? string.Empty,
            BatchFile = r.BatchFile,
            ResponseFileNumber = r.ResponseFileNumber ?? 0,
            ResponseFileName = r.ResponseFileName,
            LineNumber = r.LineNumber ?? 0,
            InputRecordLineNumber = r.InputRecordLineNumber,
            AckNumber = r.AckNumber,
            RecordStatus = r.RecordStatus,
            CkycReferenceNumber = r.CkycReferenceNumber,
            CkycNumber = r.CkycNumber,
            RejectionRemark = r.RejectionRemark,
            ReadAt = r.ReadAt,
            Remarks = r.Remarks,
            RawData = r.RawData,
            CreatedAt = r.CreatedAt ?? DateTime.MinValue,
        }).ToList();
    }

    public async Task<bool> HasUploadResponseFileAsync(string sourceHash, string responseFileName, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        return await db.UploadResponseFiles
            .AnyAsync(f => f.SourceHash == sourceHash && f.ResponseFileName == responseFileName, ct);
    }

    public async Task<bool> TryAddUploadResponseFileAsync(UploadResponseFile responseFile, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync($"CKYC:upload-response:{responseFile.SourceHash}", ct);
        var exists = await db.UploadResponseFiles
            .AnyAsync(f => f.SourceHash == responseFile.SourceHash && f.ResponseFileName == responseFile.ResponseFileName, ct);
        if (exists)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        db.UploadResponseFiles.Add(new UploadResponseFileEntity
        {
            BatchFile = responseFile.BatchFile,
            ResponseFileName = responseFile.ResponseFileName,
            ResponseFileNumber = responseFile.ResponseFileNumber,
            TotalRecords = responseFile.TotalRecords,
            TotalProcessed = responseFile.TotalProcessed,
            UnderProcessing = responseFile.UnderProcessing,
            Failed = responseFile.Failed,
            ResponseTimestamp = responseFile.ResponseTimestamp,
            RawHeaderData = responseFile.RawHeaderData,
            SourceArchiveName = responseFile.SourceArchiveName,
            SourceHash = responseFile.SourceHash,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> LogAttemptAsync(MasterRecordAttempt attempt, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var attemptedAt = attempt.AttemptedAt ?? now;
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync($"CKYC:master-attempt:{attempt.MasterRecordId}", ct);

        var next = await db.MasterRecordAttempts
            .Where(a => a.MasterRecordId == attempt.MasterRecordId && a.Stage == attempt.Stage)
            .CountAsync(ct) + 1;

        db.MasterRecordAttempts.Add(new MasterRecordAttemptEntity
        {
            MasterRecordId = attempt.MasterRecordId,
            CustomerId = attempt.CustomerId,
            Stage = attempt.Stage,
            ActivityTypeId = attempt.ActivityTypeId,
            Attempt = next,
            Status = attempt.Status,
            Success = attempt.Success ? 1 : 0,
            Error = attempt.Error,
            Remarks = attempt.Remarks,
            AttemptedAt = attemptedAt,
            NextRetryAt = attempt.NextRetryAt,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return 1;
    }

    public async Task<IReadOnlyList<ActivityType>> GetActivityTypesAsync(CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var rows = await db.ActivityTypes.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<ActivityType?> GetActivityTypeByCodeAsync(string code, CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var row = await db.ActivityTypes.AsNoTracking().SingleOrDefaultAsync(a => a.Code == code, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<StatusMaster>> GetStatusMastersAsync(CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var rows = await db.StatusMasters.AsNoTracking().OrderBy(s => s.StatusValue).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<StatusMaster?> GetStatusMasterByValueAsync(int statusValue, CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var row = await db.StatusMasters.AsNoTracking().SingleOrDefaultAsync(s => s.StatusValue == statusValue, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<MasterRecordReattempt> LogReattemptAsync(MasterRecordReattempt reattempt, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var reattemptedAt = reattempt.ReattemptedAt ?? now;
        await using var db = _db.CreateContext();
        db.MasterRecordReattempts.Add(new MasterRecordReattemptEntity
        {
            MasterRecordId = reattempt.MasterRecordId,
            CustomerId = reattempt.CustomerId,
            Reason = reattempt.Reason,
            PreviousStatus = reattempt.PreviousStatus,
            PreviousReconStatus = reattempt.PreviousReconStatus,
            PreviousResponseStatus = reattempt.PreviousResponseStatus,
            PreviousResponseAckNumber = reattempt.PreviousResponseAckNumber,
            PreviousResponseCkycReference = reattempt.PreviousResponseCkycReference,
            PreviousResponseCkycNumber = reattempt.PreviousResponseCkycNumber,
            PreviousResponseRejectionRemark = reattempt.PreviousResponseRejectionRemark,
            PreviousResponseReadAt = reattempt.PreviousResponseReadAt,
            PreviousRetryCount = reattempt.PreviousRetryCount,
            ReattemptCount = reattempt.ReattemptCount,
            ReattemptedAt = reattemptedAt,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        reattempt.CreatedAt = now;
        return reattempt;
    }

    public async Task<IReadOnlyList<MasterRecordReattempt>> GetReattemptsAsync(long masterRecordId, CancellationToken ct)
    {
        await using var db = _db.CreateContext();
        var rows = await db.MasterRecordReattempts.AsNoTracking()
            .Where(r => r.MasterRecordId == masterRecordId)
            .OrderBy(r => r.Id).ToListAsync(ct);
        return rows.Select(r => new MasterRecordReattempt
        {
            Id = r.Id,
            MasterRecordId = r.MasterRecordId ?? 0,
            CustomerId = r.CustomerId ?? string.Empty,
            Reason = r.Reason,
            PreviousStatus = r.PreviousStatus,
            PreviousReconStatus = r.PreviousReconStatus,
            PreviousResponseStatus = r.PreviousResponseStatus,
            PreviousResponseAckNumber = r.PreviousResponseAckNumber,
            PreviousResponseCkycReference = r.PreviousResponseCkycReference,
            PreviousResponseCkycNumber = r.PreviousResponseCkycNumber,
            PreviousResponseRejectionRemark = r.PreviousResponseRejectionRemark,
            PreviousResponseReadAt = r.PreviousResponseReadAt,
            PreviousRetryCount = r.PreviousRetryCount,
            ReattemptCount = r.ReattemptCount ?? 0,
            ReattemptedAt = r.ReattemptedAt,
            CreatedAt = r.CreatedAt ?? DateTime.MinValue,
        }).ToList();
    }

    public async Task<bool> ResetForReattemptAsync(long id, string remarks, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        var row = await db.MasterRecords.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return false;

        row.Status = (int)MasterRecordStatus.Saved;
        row.StatusCode = MasterRecordStatusCode.For(MasterRecordStatus.Saved);
        row.IsRejected = 0;
        row.IsUploaded = 0;
        row.RetryCount = 0;
        row.LastError = null;
        row.LastActivity = null;
        row.NextRetryAt = null;
        row.NeedsReconcile = 0;
        row.ReattemptCount = (row.ReattemptCount ?? 0) + 1;
        row.ReattemptedAt = now;
        row.Remarks = remarks;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static (string? Flag, string? Timestamp) StageFor(MasterRecordStatus status) => status switch
    {
        MasterRecordStatus.CrmFetched => ("IsCrmFetched", "CrmFetchedAt"),
        MasterRecordStatus.Saved => ("IsSaved", "SavedAt"),
        MasterRecordStatus.Batched => ("IsBatched", "BatchedAt"),
        MasterRecordStatus.Uploaded => ("IsUploaded", "UploadedAt"),
        MasterRecordStatus.ResponseRead => ("IsResponseRead", "FirstResponseAt"),
        MasterRecordStatus.Reconciled => ("IsReconciled", "ReconciledAt"),
        MasterRecordStatus.Rejected => ("IsRejected", null),
        _ => (null, null),
    };

    private static MasterRecord ToDomain(MasterRecordEntity r) => new()
    {
        Id = r.Id,
        CustomerId = r.CustomerId ?? string.Empty,
        ClientType = r.ClientType ?? "I",
        BusinessDate = r.BusinessDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue,
        Status = (MasterRecordStatus)(r.Status ?? 0),
        StatusCode = r.StatusCode ?? MasterRecordStatusCode.Pending,
        Remarks = r.Remarks,
        RetryCount = r.RetryCount,
        LastError = r.LastError,
        LastAttemptAt = r.LastAttemptAt,
        LastActivity = r.LastActivity,
        NextRetryAt = r.NextRetryAt,
        NeedsReconcile = r.NeedsReconcile == 1,
        ReattemptCount = r.ReattemptCount ?? 0,
        ReattemptedAt = r.ReattemptedAt,
        BatchFile = r.BatchFile,
        BatchRecordLine = r.BatchRecordLine,
        IsCrmFetched = r.IsCrmFetched == 1,
        IsSaved = r.IsSaved == 1,
        IsBatched = r.IsBatched == 1,
        IsUploaded = r.IsUploaded == 1,
        IsResponseRead = r.IsResponseRead == 1,
        IsReconciled = r.IsReconciled == 1,
        IsRejected = r.IsRejected == 1,
        CrmFetchedAt = r.CrmFetchedAt,
        SavedAt = r.SavedAt,
        BatchedAt = r.BatchedAt,
        UploadedAt = r.UploadedAt,
        FirstResponseAt = r.FirstResponseAt,
        ReconciledAt = r.ReconciledAt,
        LastResponseFileNumber = r.LastResponseFileNumber,
        LastResponseFileName = r.LastResponseFileName,
        LastResponseAckNumber = r.LastResponseAckNumber,
        LastResponseStatus = r.LastResponseStatus,
        LastResponseCkycReference = r.LastResponseCkycReference,
        LastResponseCkycNumber = r.LastResponseCkycNumber,
        LastResponseRejectionRemark = r.LastResponseRejectionRemark,
        LastResponseReadAt = r.LastResponseReadAt,
        LastResponseRemarks = r.LastResponseRemarks,
        ReconStatus = r.ReconStatus,
        ReconRemarks = r.ReconRemarks,
        CreatedAt = r.CreatedAt ?? DateTime.MinValue,
        UpdatedAt = r.UpdatedAt ?? DateTime.MinValue,
    };

    private static ActivityType ToDomain(ActivityTypeEntity a) => new()
    {
        Id = a.Id,
        Code = a.Code ?? string.Empty,
        Name = a.Name ?? string.Empty,
        IsRetryable = a.IsRetryable == 1,
        MaxAttempts = a.MaxAttempts ?? 3,
        BackoffBaseHours = a.BackoffBaseHours ?? 24,
        BackoffMultiplier = a.BackoffMultiplier ?? 2.0,
        IsActive = a.IsActive == 1,
        Remarks = a.Remarks,
        CreatedAt = a.CreatedAt ?? DateTime.MinValue,
    };

    private static StatusMaster ToDomain(StatusMasterEntity s) => new()
    {
        Id = s.Id,
        StatusValue = s.StatusValue ?? 0,
        Code = s.Code ?? string.Empty,
        Name = s.Name ?? string.Empty,
        Description = s.Description,
        IsTerminal = s.IsTerminal == 1,
        IsActive = s.IsActive == 1,
        CreatedAt = s.CreatedAt ?? DateTime.MinValue,
    };
}
