using System.Data;
using System.Data.Common;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Data;

public sealed class MasterRepository : IMasterRepository
{
    private readonly ICkycDatabase _db;

    public MasterRepository(ICkycDatabase db) => _db = db;

    public async Task<FetchResult> UpsertDailyAsync(IReadOnlyCollection<string> customerIds, DateOnly businessDate, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return new FetchResult(0, 0, 0);

        await using var conn = _db.Create();
        await using var existsCmd = conn.CreateCommand();
        existsCmd.CommandText = "SELECT SourceCustomerId FROM master_record";
        var existing = new HashSet<string>(StringComparer.Ordinal);
        await using (var r = await existsCmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) existing.Add(r.GetString(0));
        }

        var now = DateTime.UtcNow.ToString("o");
        var business = businessDate.ToString("yyyy-MM-dd");
        int inserted = 0, skipped = 0;
        foreach (var id in customerIds)
        {
            if (existing.Contains(id)) { skipped++; continue; }
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO master_record
                    (SourceCustomerId, BusinessDate, Status, Remarks, RetryCount, CreatedAt, UpdatedAt)
                VALUES
                    (@id, @date, 0, @remarks, 0, @now, @now)
                """;
            cmd.Parameters.Add(NewParam("@id", id));
            cmd.Parameters.Add(NewParam("@date", business));
            cmd.Parameters.Add(NewParam("@remarks", $"Fetched on {businessDate:dd-MM-yyyy}"));
            cmd.Parameters.Add(NewParam("@now", now));
            await cmd.ExecuteNonQueryAsync(ct);
            inserted++;
        }
        return new FetchResult(inserted, skipped, customerIds.Count);
    }

    public async Task<IReadOnlyList<MasterRecord>> GetByStatusAsync(MasterRecordStatus status, int limit, string? clientType = null, CancellationToken ct = default)
    {
        var filter = string.IsNullOrWhiteSpace(clientType) ? "" : " AND ClientType=@ct";
        return await QueryAsync($"SELECT * FROM master_record WHERE Status=@s{filter} ORDER BY Id LIMIT @n",
            c =>
            {
                c.Parameters.Add(NewParam("@s", (int)status));
                c.Parameters.Add(NewParam("@n", limit));
                if (!string.IsNullOrWhiteSpace(clientType)) c.Parameters.Add(NewParam("@ct", clientType));
            }, ct);
    }

    public async Task<IReadOnlyList<MasterRecord>> GetRetryableAsync(int maxRetries, int limit, CancellationToken ct = default)
        => await QueryAsync(
            "SELECT * FROM master_record WHERE Status=@failed AND RetryCount < @max ORDER BY Id LIMIT @n",
            c => { c.Parameters.Add(NewParam("@failed", (int)MasterRecordStatus.Failed));
                   c.Parameters.Add(NewParam("@max", maxRetries));
                   c.Parameters.Add(NewParam("@n", limit)); }, ct);

    public async Task<IReadOnlyList<MasterRecord>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return Array.Empty<MasterRecord>();
        var placeholders = string.Join(",", customerIds.Select((_, i) => $"@v{i}"));
        return await QueryAsync($"SELECT * FROM master_record WHERE SourceCustomerId IN ({placeholders})",
            c => { var i = 0; foreach (var id in customerIds) c.Parameters.Add(NewParam($"@v{i++}", id)); }, ct);
    }

    public async Task<MasterRecord?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var rows = await QueryAsync("SELECT * FROM master_record WHERE Id=@id",
            c => c.Parameters.Add(NewParam("@id", id)), ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<IReadOnlyList<MasterRecord>> GetByBatchFileAsync(string batchFile, CancellationToken ct = default)
        => await QueryAsync("SELECT * FROM master_record WHERE BatchFile=@b ORDER BY Id",
            c => c.Parameters.Add(NewParam("@b", batchFile)), ct);

    public async Task<MasterRecord?> GetByBatchLineAsync(string batchFile, int record20Line, CancellationToken ct = default)
    {
        var rows = await QueryAsync("SELECT * FROM master_record WHERE BatchFile=@b AND BatchRecordLine=@l",
            c => { c.Parameters.Add(NewParam("@b", batchFile)); c.Parameters.Add(NewParam("@l", record20Line)); }, ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<MasterRecord> EnsureAsync(string customerId, DateOnly businessDate, string? clientType = null, CancellationToken ct = default)
    {
        var existing = await GetByCustomerIdsAsync(new[] { customerId }, ct);
        if (existing.Count > 0) return existing[0];

        var now = DateTime.UtcNow.ToString("o");
        var business = businessDate.ToString("yyyy-MM-dd");
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO master_record
                (SourceCustomerId, ClientType, BusinessDate, Status, Remarks, RetryCount, CreatedAt, UpdatedAt)
            VALUES
                (@id, @ct, @date, 0, @remarks, 0, @now, @now)
            """;
        cmd.Parameters.Add(NewParam("@id", customerId));
        cmd.Parameters.Add(NewParam("@ct", string.IsNullOrWhiteSpace(clientType) ? "I" : clientType));
        cmd.Parameters.Add(NewParam("@date", business));
        cmd.Parameters.Add(NewParam("@remarks", $"Inserted on {businessDate:dd-MM-yyyy}"));
        cmd.Parameters.Add(NewParam("@now", now));
        await cmd.ExecuteNonQueryAsync(ct);

        var rows = await QueryAsync("SELECT * FROM master_record WHERE SourceCustomerId=@id",
            c => c.Parameters.Add(NewParam("@id", customerId)), ct);
        return rows[0];
    }

    /// <summary>
    /// Transitions the record to <paramref name="status"/> and, when that status maps to a
    /// pipeline stage, sets the matching <c>Is*</c> flag and first-reached timestamp.
    /// </summary>
    public async Task<bool> UpdateStatusAsync(long id, MasterRecordStatus status, string? remarks, string? lastError, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("o");
        var (flag, timestamp) = StageFor(status);

        var set = new StringBuilder(
            "SET Status=@s, Remarks=@remarks, LastError=@err, LastAttemptAt=@attempt, UpdatedAt=@upd");
        if (flag is not null) set.Append($", {flag}=1");
        // Preserve the FIRST time each stage was reached (a stage is only ever reached once).
        if (timestamp is not null) set.Append($", {timestamp}=COALESCE({timestamp}, @now)");

        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE master_record {set} WHERE Id=@id";
        cmd.Parameters.Add(NewParam("@s", (int)status));
        cmd.Parameters.Add(NewParam("@remarks", remarks));
        cmd.Parameters.Add(NewParam("@err", lastError));
        cmd.Parameters.Add(NewParam("@attempt", now));
        cmd.Parameters.Add(NewParam("@upd", now));
        cmd.Parameters.Add(NewParam("@now", now));
        cmd.Parameters.Add(NewParam("@id", id));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> IncrementRetryAsync(long id, string? lastError, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE master_record
               SET RetryCount=RetryCount+1, LastError=@err, LastAttemptAt=@attempt, UpdatedAt=@upd
             WHERE Id=@id
            """;
        cmd.Parameters.Add(NewParam("@err", lastError));
        cmd.Parameters.Add(NewParam("@attempt", DateTime.UtcNow.ToString("o")));
        cmd.Parameters.Add(NewParam("@upd", DateTime.UtcNow.ToString("o")));
        cmd.Parameters.Add(NewParam("@id", id));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<int> MarkBatchAsync(IReadOnlyCollection<long> ids, string batchFile,
        IReadOnlyDictionary<long, int>? lineByRecord, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var localTx = (DbTransaction)tx;
        var placeholders = string.Join(",", ids.Select((_, i) => $"@v{i}"));
        var now = DateTime.UtcNow.ToString("o");

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = localTx;
            cmd.CommandText = $"UPDATE master_record SET Status=@s, BatchFile=@bf, IsBatched=1, BatchedAt=COALESCE(BatchedAt,@now), UpdatedAt=@upd WHERE Id IN ({placeholders})";
            cmd.Parameters.Add(NewParam("@s", (int)MasterRecordStatus.Batched));
            cmd.Parameters.Add(NewParam("@bf", batchFile));
            cmd.Parameters.Add(NewParam("@now", now));
            cmd.Parameters.Add(NewParam("@upd", now));
            var i2 = 0;
            foreach (var id in ids) cmd.Parameters.Add(NewParam($"@v{i2++}", id));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (lineByRecord is { Count: > 0 })
        {
            foreach (var (recordId, line) in lineByRecord)
            {
                await using var lcmd = conn.CreateCommand();
                lcmd.Transaction = localTx;
                lcmd.CommandText = "UPDATE master_record SET BatchRecordLine=@l WHERE Id=@id";
                lcmd.Parameters.Add(NewParam("@l", line));
                lcmd.Parameters.Add(NewParam("@id", recordId));
                await lcmd.ExecuteNonQueryAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
        return ids.Count;
    }

    public async Task<int> CountByStatusAsync(MasterRecordStatus status, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM master_record WHERE Status=@s";
        cmd.Parameters.Add(NewParam("@s", (int)status));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Persists one CERSAI reply detail into <c>master_record_response</c> and mirrors the
    /// latest values onto the master row (status → <see cref="MasterRecordStatus.ResponseRead"/>,
    /// <c>IsResponseRead</c>, first-response timestamp and the <c>LastResponse*</c> summary).
    /// </summary>
    public async Task<MasterRecordResponse> AddResponseAsync(MasterRecordResponse response, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");
        var readAt = response.ReadAt ?? now;

        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var localTx = (DbTransaction)tx;

        // Idempotent re-read: if this (record, response file, line) was already read, replace
        // it rather than stacking duplicates, then re-apply the latest summary to the master row.
        await using (var del = conn.CreateCommand())
        {
            del.Transaction = localTx;
            del.CommandText = """
                DELETE FROM master_record_response
                 WHERE MasterRecordId=@mid AND ResponseFileName=@rfile AND LineNumber=@ln
                """;
            del.Parameters.Add(NewParam("@mid", response.MasterRecordId));
            del.Parameters.Add(NewParam("@rfile", response.ResponseFileName));
            del.Parameters.Add(NewParam("@ln", response.LineNumber));
            await del.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = localTx;
            cmd.CommandText = """
                INSERT INTO master_record_response
                    (MasterRecordId, SourceCustomerId, BatchFile, ResponseFileNumber, ResponseFileName, LineNumber,
                     InputRecordLineNumber, AckNumber, RecordStatus, CkycReferenceNumber, CkycNumber, RejectionRemark,
                     ReadAt, Remarks, RawData, CreatedAt)
                VALUES
                    (@mid, @sid, @bf, @rfno, @rfile, @ln, @inln, @ack, @st, @cref, @ckyc, @rej, @read, @rm, @raw, @now)
                """;
            cmd.Parameters.Add(NewParam("@mid", response.MasterRecordId));
            cmd.Parameters.Add(NewParam("@sid", response.SourceCustomerId));
            cmd.Parameters.Add(NewParam("@bf", response.BatchFile));
            cmd.Parameters.Add(NewParam("@rfno", response.ResponseFileNumber));
            cmd.Parameters.Add(NewParam("@rfile", response.ResponseFileName));
            cmd.Parameters.Add(NewParam("@ln", response.LineNumber));
            cmd.Parameters.Add(NewParam("@inln", response.InputRecordLineNumber));
            cmd.Parameters.Add(NewParam("@ack", response.AckNumber));
            cmd.Parameters.Add(NewParam("@st", response.RecordStatus));
            cmd.Parameters.Add(NewParam("@cref", response.CkycReferenceNumber));
            cmd.Parameters.Add(NewParam("@ckyc", response.CkycNumber));
            cmd.Parameters.Add(NewParam("@rej", response.RejectionRemark));
            cmd.Parameters.Add(NewParam("@read", readAt.ToString("o")));
            cmd.Parameters.Add(NewParam("@rm", response.Remarks));
            cmd.Parameters.Add(NewParam("@raw", response.RawData));
            cmd.Parameters.Add(NewParam("@now", nowStr));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var u = conn.CreateCommand())
        {
            u.Transaction = localTx;
            u.CommandText = """
                UPDATE master_record
                   SET Status=@st, IsResponseRead=1, FirstResponseAt=COALESCE(FirstResponseAt,@now),
                       LastResponseFileNumber=@rfno, LastResponseFileName=@rfile, LastResponseAckNumber=@ack,
                       LastResponseStatus=@rst, LastResponseCkycReference=@cref, LastResponseCkycNumber=@ckyc,
                       LastResponseRejectionRemark=@rej, LastResponseReadAt=@read, LastResponseRemarks=@rm,
                       UpdatedAt=@now
                 WHERE Id=@mid
                """;
            u.Parameters.Add(NewParam("@st", (int)MasterRecordStatus.ResponseRead));
            u.Parameters.Add(NewParam("@rst", response.RecordStatus));
            u.Parameters.Add(NewParam("@mid", response.MasterRecordId));
            u.Parameters.Add(NewParam("@now", nowStr));
            u.Parameters.Add(NewParam("@rfno", response.ResponseFileNumber));
            u.Parameters.Add(NewParam("@rfile", response.ResponseFileName));
            u.Parameters.Add(NewParam("@ack", response.AckNumber));
            u.Parameters.Add(NewParam("@cref", response.CkycReferenceNumber));
            u.Parameters.Add(NewParam("@ckyc", response.CkycNumber));
            u.Parameters.Add(NewParam("@rej", response.RejectionRemark));
            u.Parameters.Add(NewParam("@read", readAt.ToString("o")));
            u.Parameters.Add(NewParam("@rm", response.Remarks));
            await u.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        response.CreatedAt = now;
        return response;
    }

    public async Task<IReadOnlyList<MasterRecordResponse>> GetResponsesAsync(long masterRecordId, CancellationToken ct = default)
    {
        var result = new List<MasterRecordResponse>();
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM master_record_response WHERE MasterRecordId=@m ORDER BY ResponseFileNumber, LineNumber";
        cmd.Parameters.Add(NewParam("@m", masterRecordId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new MasterRecordResponse
            {
                Id = r.GetInt64(r.GetOrdinal("Id")),
                MasterRecordId = Convert.ToInt64(r["MasterRecordId"]),
                SourceCustomerId = r["SourceCustomerId"] as string ?? string.Empty,
                BatchFile = r["BatchFile"] as string,
                ResponseFileNumber = Convert.ToInt32(r["ResponseFileNumber"]),
                ResponseFileName = r["ResponseFileName"] as string,
                LineNumber = Convert.ToInt32(r["LineNumber"]),
                InputRecordLineNumber = r["InputRecordLineNumber"] is DBNull ? null : Convert.ToInt32(r["InputRecordLineNumber"]),
                AckNumber = r["AckNumber"] as string,
                RecordStatus = r["RecordStatus"] as string,
                CkycReferenceNumber = r["CkycReferenceNumber"] as string,
                CkycNumber = r["CkycNumber"] as string,
                RejectionRemark = r["RejectionRemark"] as string,
                ReadAt = ReadNullableDate(r, "ReadAt"),
                Remarks = r["Remarks"] as string,
                RawData = r["RawData"] as string,
                CreatedAt = ReadDate(r, "CreatedAt"),
            });
        }
        return result;
    }

    public async Task<int> LogAttemptAsync(MasterRecordAttempt attempt, CancellationToken ct = default)
    {
        var attemptNo = await NextAttemptNumberAsync(attempt.MasterRecordId, attempt.Stage, ct);
        var now = DateTime.UtcNow;
        var attemptedAt = attempt.AttemptedAt ?? now;

        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO master_record_attempt
                (MasterRecordId, SourceCustomerId, Stage, ActivityTypeId, Attempt, Status, Success, Error, Remarks, AttemptedAt, NextRetryAt, CreatedAt)
            VALUES
                (@mid, @sid, @stage, @atid, @attempt, @st, @ok, @err, @rm, @at, @next, @now)
            """;
        cmd.Parameters.Add(NewParam("@mid", attempt.MasterRecordId));
        cmd.Parameters.Add(NewParam("@sid", attempt.SourceCustomerId));
        cmd.Parameters.Add(NewParam("@stage", attempt.Stage));
        cmd.Parameters.Add(NewParam("@atid", attempt.ActivityTypeId));
        cmd.Parameters.Add(NewParam("@attempt", attemptNo));
        cmd.Parameters.Add(NewParam("@st", attempt.Status));
        cmd.Parameters.Add(NewParam("@ok", attempt.Success ? 1 : 0));
        cmd.Parameters.Add(NewParam("@err", attempt.Error));
        cmd.Parameters.Add(NewParam("@rm", attempt.Remarks));
        cmd.Parameters.Add(NewParam("@at", attemptedAt.ToString("o")));
        cmd.Parameters.Add(NewParam("@next", attempt.NextRetryAt is { } n ? n.ToString("o") : null));
        cmd.Parameters.Add(NewParam("@now", now.ToString("o")));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> RecordRetryAsync(long id, int retryCount, string? lastError, string? lastActivity,
        DateTime? nextRetryAt, bool needsReconcile, CancellationToken ct)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE master_record
               SET RetryCount=@count, LastError=@err, LastActivity=@activity,
                   LastAttemptAt=@attempt, NextRetryAt=@next, NeedsReconcile=@needs, UpdatedAt=@upd
             WHERE Id=@id
            """;
        cmd.Parameters.Add(NewParam("@count", retryCount));
        cmd.Parameters.Add(NewParam("@err", lastError));
        cmd.Parameters.Add(NewParam("@activity", lastActivity));
        cmd.Parameters.Add(NewParam("@attempt", DateTime.UtcNow.ToString("o")));
        cmd.Parameters.Add(NewParam("@next", nextRetryAt is { } n ? n.ToString("o") : null));
        cmd.Parameters.Add(NewParam("@needs", needsReconcile ? 1 : 0));
        cmd.Parameters.Add(NewParam("@upd", DateTime.UtcNow.ToString("o")));
        cmd.Parameters.Add(NewParam("@id", id));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> MarkNeedsReconcileAsync(long id, string reason, CancellationToken ct)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE master_record
               SET NeedsReconcile=1, ReconStatus=@status, ReconRemarks=@remarks, UpdatedAt=@upd
             WHERE Id=@id
            """;
        cmd.Parameters.Add(NewParam("@status", "NeedsIntervention"));
        cmd.Parameters.Add(NewParam("@remarks", reason));
        cmd.Parameters.Add(NewParam("@upd", DateTime.UtcNow.ToString("o")));
        cmd.Parameters.Add(NewParam("@id", id));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> ClearRetryStateAsync(long id, CancellationToken ct)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE master_record
               SET RetryCount=0, LastError=NULL, LastActivity=NULL, NextRetryAt=NULL,
                   NeedsReconcile=0, UpdatedAt=@upd
             WHERE Id=@id
            """;
        cmd.Parameters.Add(NewParam("@upd", DateTime.UtcNow.ToString("o")));
        cmd.Parameters.Add(NewParam("@id", id));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<MasterRecord>> GetRetryableForActivityAsync(string activityCode, int maxAttempts,
        DateTime now, int limit, CancellationToken ct)
        => await QueryAsync(
            """
            SELECT * FROM master_record
             WHERE Status=@failed AND RetryCount < @max AND LastActivity=@activity
               AND (NextRetryAt IS NULL OR NextRetryAt <= @now)
             ORDER BY Id LIMIT @n
            """,
            c =>
            {
                c.Parameters.Add(NewParam("@failed", (int)MasterRecordStatus.Failed));
                c.Parameters.Add(NewParam("@max", maxAttempts));
                c.Parameters.Add(NewParam("@activity", activityCode));
                c.Parameters.Add(NewParam("@now", now.ToString("o")));
                c.Parameters.Add(NewParam("@n", limit));
            }, ct);

    public async Task<IReadOnlyList<MasterRecord>> GetNeedsReconcileAsync(string? kind, int limit, CancellationToken ct)
    {
        var sql = kind switch
        {
            "retry" => """
                SELECT * FROM master_record
                 WHERE NeedsReconcile=1 AND (Status=@failed OR Status=@rejected)
                 ORDER BY Id LIMIT @n
                """,
            "cersai" => """
                SELECT * FROM master_record
                 WHERE Status=@rejected OR IsRejected=1 OR Status=@fvufailed
                 ORDER BY Id LIMIT @n
                """,
            _ => """
                SELECT * FROM master_record
                 WHERE NeedsReconcile=1 OR Status=@rejected OR IsRejected=1 OR Status=@fvufailed
                 ORDER BY Id LIMIT @n
                """,
        };
        return await QueryAsync(sql, c =>
        {
            c.Parameters.Add(NewParam("@failed", (int)MasterRecordStatus.Failed));
            c.Parameters.Add(NewParam("@rejected", (int)MasterRecordStatus.Rejected));
            c.Parameters.Add(NewParam("@fvufailed", (int)MasterRecordStatus.FvuFailed));
            c.Parameters.Add(NewParam("@n", limit));
        }, ct);
    }

    public async Task<IReadOnlyList<ActivityType>> GetActivityTypesAsync(CancellationToken ct)
    {
        var result = new List<ActivityType>();
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM activity_type ORDER BY Id";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(ReadActivityType(r));
        return result;
    }

    public async Task<ActivityType?> GetActivityTypeByCodeAsync(string code, CancellationToken ct)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM activity_type WHERE Code=@c";
        cmd.Parameters.Add(NewParam("@c", code));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadActivityType(r) : null;
    }

    public async Task<IReadOnlyList<StatusMaster>> GetStatusMastersAsync(CancellationToken ct)
    {
        var result = new List<StatusMaster>();
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM status_master ORDER BY StatusValue";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(ReadStatusMaster(r));
        return result;
    }

    public async Task<StatusMaster?> GetStatusMasterByValueAsync(int statusValue, CancellationToken ct)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM status_master WHERE StatusValue=@v";
        cmd.Parameters.Add(NewParam("@v", statusValue));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadStatusMaster(r) : null;
    }

    public async Task<MasterRecordReattempt> LogReattemptAsync(MasterRecordReattempt reattempt, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var reattemptedAt = reattempt.ReattemptedAt ?? now;
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO master_record_reattempt
                (MasterRecordId, SourceCustomerId, Reason, PreviousStatus, PreviousReconStatus,
                 PreviousResponseStatus, PreviousResponseAckNumber, PreviousResponseCkycReference,
                 PreviousResponseCkycNumber, PreviousResponseRejectionRemark, PreviousResponseReadAt,
                 PreviousRetryCount, ReattemptCount, ReattemptedAt, CreatedAt)
            VALUES
                (@mid, @sid, @reason, @pstatus, @precon, @prstatus, @pack, @pcref, @pckyc,
                 @prej, @pread, @pretry, @rcount, @rat, @now)
            """;
        cmd.Parameters.Add(NewParam("@mid", reattempt.MasterRecordId));
        cmd.Parameters.Add(NewParam("@sid", reattempt.SourceCustomerId));
        cmd.Parameters.Add(NewParam("@reason", reattempt.Reason));
        cmd.Parameters.Add(NewParam("@pstatus", reattempt.PreviousStatus));
        cmd.Parameters.Add(NewParam("@precon", reattempt.PreviousReconStatus));
        cmd.Parameters.Add(NewParam("@prstatus", reattempt.PreviousResponseStatus));
        cmd.Parameters.Add(NewParam("@pack", reattempt.PreviousResponseAckNumber));
        cmd.Parameters.Add(NewParam("@pcref", reattempt.PreviousResponseCkycReference));
        cmd.Parameters.Add(NewParam("@pckyc", reattempt.PreviousResponseCkycNumber));
        cmd.Parameters.Add(NewParam("@prej", reattempt.PreviousResponseRejectionRemark));
        cmd.Parameters.Add(NewParam("@pread", reattempt.PreviousResponseReadAt is { } d ? d.ToString("o") : null));
        cmd.Parameters.Add(NewParam("@pretry", reattempt.PreviousRetryCount));
        cmd.Parameters.Add(NewParam("@rcount", reattempt.ReattemptCount));
        cmd.Parameters.Add(NewParam("@rat", reattemptedAt.ToString("o")));
        cmd.Parameters.Add(NewParam("@now", now.ToString("o")));
        await cmd.ExecuteNonQueryAsync(ct);
        reattempt.CreatedAt = now;
        return reattempt;
    }

    public async Task<IReadOnlyList<MasterRecordReattempt>> GetReattemptsAsync(long masterRecordId, CancellationToken ct)
    {
        var result = new List<MasterRecordReattempt>();
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM master_record_reattempt WHERE MasterRecordId=@m ORDER BY Id";
        cmd.Parameters.Add(NewParam("@m", masterRecordId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new MasterRecordReattempt
            {
                Id = r.GetInt64(r.GetOrdinal("Id")),
                MasterRecordId = Convert.ToInt64(r["MasterRecordId"]),
                SourceCustomerId = r["SourceCustomerId"] as string ?? string.Empty,
                Reason = r["Reason"] as string,
                PreviousStatus = r["PreviousStatus"] is DBNull ? null : Convert.ToInt32(r["PreviousStatus"]),
                PreviousReconStatus = r["PreviousReconStatus"] as string,
                PreviousResponseStatus = r["PreviousResponseStatus"] as string,
                PreviousResponseAckNumber = r["PreviousResponseAckNumber"] as string,
                PreviousResponseCkycReference = r["PreviousResponseCkycReference"] as string,
                PreviousResponseCkycNumber = r["PreviousResponseCkycNumber"] as string,
                PreviousResponseRejectionRemark = r["PreviousResponseRejectionRemark"] as string,
                PreviousResponseReadAt = ReadNullableDate(r, "PreviousResponseReadAt"),
                PreviousRetryCount = r["PreviousRetryCount"] is DBNull ? null : Convert.ToInt32(r["PreviousRetryCount"]),
                ReattemptCount = Convert.ToInt32(r["ReattemptCount"]),
                ReattemptedAt = ReadNullableDate(r, "ReattemptedAt"),
                CreatedAt = ReadDate(r, "CreatedAt"),
            });
        }
        return result;
    }

    public async Task<bool> ResetForReattemptAsync(long id, string remarks, CancellationToken ct)
    {
        var now = DateTime.UtcNow.ToString("o");
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE master_record
               SET Status=@saved, IsRejected=0, IsUploaded=0, RetryCount=0, LastError=NULL,
                   LastActivity=NULL, NextRetryAt=NULL, NeedsReconcile=0,
                   ReattemptCount=ReattemptCount+1, ReattemptedAt=@rat, Remarks=@remarks, UpdatedAt=@upd
             WHERE Id=@id
            """;
        cmd.Parameters.Add(NewParam("@saved", (int)MasterRecordStatus.Saved));
        cmd.Parameters.Add(NewParam("@rat", now));
        cmd.Parameters.Add(NewParam("@remarks", remarks));
        cmd.Parameters.Add(NewParam("@upd", now));
        cmd.Parameters.Add(NewParam("@id", id));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static ActivityType ReadActivityType(DbDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        Code = r["Code"] as string ?? string.Empty,
        Name = r["Name"] as string ?? string.Empty,
        IsRetryable = ReadBool(r, "IsRetryable"),
        MaxAttempts = Convert.ToInt32(r["MaxAttempts"]),
        BackoffBaseHours = Convert.ToInt32(r["BackoffBaseHours"]),
        BackoffMultiplier = Convert.ToDouble(r["BackoffMultiplier"]),
        IsActive = ReadBool(r, "IsActive"),
        Remarks = r["Remarks"] as string,
        CreatedAt = ReadDate(r, "CreatedAt"),
    };

    private static StatusMaster ReadStatusMaster(DbDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        StatusValue = Convert.ToInt32(r["StatusValue"]),
        Code = r["Code"] as string ?? string.Empty,
        Name = r["Name"] as string ?? string.Empty,
        Description = r["Description"] as string,
        IsTerminal = ReadBool(r, "IsTerminal"),
        IsActive = ReadBool(r, "IsActive"),
        CreatedAt = ReadDate(r, "CreatedAt"),
    };

    private async Task<int> NextAttemptNumberAsync(long masterRecordId, string stage, CancellationToken ct)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM master_record_attempt WHERE MasterRecordId=@m AND Stage=@s";
        cmd.Parameters.Add(NewParam("@m", masterRecordId));
        cmd.Parameters.Add(NewParam("@s", stage));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) + 1;
    }

    private async Task<IReadOnlyList<MasterRecord>> QueryAsync(string sql, Action<DbCommand> configure, CancellationToken ct)
    {
        var result = new List<MasterRecord>();
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configure(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new MasterRecord
            {
                Id = r.GetInt64(r.GetOrdinal("Id")),
                SourceCustomerId = r["SourceCustomerId"] as string ?? string.Empty,
                BusinessDate = ReadDate(r, "BusinessDate"),
                Status = (MasterRecordStatus)Convert.ToInt32(r["Status"]),
                Remarks = r["Remarks"] as string,
                RetryCount = Convert.ToInt32(r["RetryCount"]),
                LastError = r["LastError"] as string,
                LastAttemptAt = ReadNullableDate(r, "LastAttemptAt"),
                LastActivity = r["LastActivity"] as string,
                NextRetryAt = ReadNullableDate(r, "NextRetryAt"),
                NeedsReconcile = ReadBool(r, "NeedsReconcile"),
                ReattemptCount = r["ReattemptCount"] is DBNull ? 0 : Convert.ToInt32(r["ReattemptCount"]),
                ReattemptedAt = ReadNullableDate(r, "ReattemptedAt"),
                BatchFile = r["BatchFile"] as string,
                BatchRecordLine = r["BatchRecordLine"] is DBNull ? null : Convert.ToInt32(r["BatchRecordLine"]),
                IsCrmFetched = ReadBool(r, "IsCrmFetched"),
                IsSaved = ReadBool(r, "IsSaved"),
                IsBatched = ReadBool(r, "IsBatched"),
                IsUploaded = ReadBool(r, "IsUploaded"),
                IsResponseRead = ReadBool(r, "IsResponseRead"),
                IsReconciled = ReadBool(r, "IsReconciled"),
                IsRejected = ReadBool(r, "IsRejected"),
                CrmFetchedAt = ReadNullableDate(r, "CrmFetchedAt"),
                SavedAt = ReadNullableDate(r, "SavedAt"),
                BatchedAt = ReadNullableDate(r, "BatchedAt"),
                UploadedAt = ReadNullableDate(r, "UploadedAt"),
                FirstResponseAt = ReadNullableDate(r, "FirstResponseAt"),
                ReconciledAt = ReadNullableDate(r, "ReconciledAt"),
                LastResponseFileNumber = r["LastResponseFileNumber"] is DBNull ? null : Convert.ToInt32(r["LastResponseFileNumber"]),
                LastResponseFileName = r["LastResponseFileName"] as string,
                LastResponseAckNumber = r["LastResponseAckNumber"] as string,
                LastResponseStatus = r["LastResponseStatus"] as string,
                LastResponseCkycReference = r["LastResponseCkycReference"] as string,
                LastResponseCkycNumber = r["LastResponseCkycNumber"] as string,
                LastResponseRejectionRemark = r["LastResponseRejectionRemark"] as string,
                LastResponseReadAt = ReadNullableDate(r, "LastResponseReadAt"),
                LastResponseRemarks = r["LastResponseRemarks"] as string,
                ReconStatus = r["ReconStatus"] as string,
                ReconRemarks = r["ReconRemarks"] as string,
                CreatedAt = ReadDate(r, "CreatedAt"),
                UpdatedAt = ReadDate(r, "UpdatedAt"),
            });
        }
        return result;
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

    private static bool ReadBool(DbDataReader r, string col)
        => r[col] is not DBNull && Convert.ToInt32(r[col]) != 0;

    private static DateTime ReadDate(DbDataReader r, string col)
    {
        var v = r[col] as string;
        return DateTime.TryParse(v, out var d) ? d : DateTime.MinValue;
    }

    private static DateTime? ReadNullableDate(DbDataReader r, string col)
    {
        var v = r[col] as string;
        return DateTime.TryParse(v, out var d) ? d : null;
    }

    internal static DbParameter NewParam(string name, object? value)
    {
        var p = new Microsoft.Data.Sqlite.SqliteParameter(name, value ?? DBNull.Value);
        return p;
    }
}
