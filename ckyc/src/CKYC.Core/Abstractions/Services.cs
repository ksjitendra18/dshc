using System.Data.Common;
using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Core.Abstractions;

/// <summary>Creates database connections and owns schema bootstrap.</summary>
public interface ICkycDatabase
{
    DbConnection Create();
    string ConnectionString { get; }
    bool IsSqlite { get; }
    Task InitializeSchemaAsync(CancellationToken ct = default);
}

/// <summary>Master table operations (step 1 source fetch + retry + stage/response tracking).</summary>
public interface IMasterRepository
{
    Task<FetchResult> UpsertDailyAsync(IReadOnlyCollection<string> customerIds, DateOnly businessDate, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecord>> GetByStatusAsync(MasterRecordStatus status, int limit, string? clientType = null, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecord>> GetRetryableAsync(int maxRetries, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecord>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default);
    Task<MasterRecord?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<MasterRecord> EnsureAsync(string customerId, DateOnly businessDate, string? clientType = null, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecord>> GetByBatchFileAsync(string batchFile, CancellationToken ct = default);
    Task<MasterRecord?> GetByBatchLineAsync(string batchFile, int record20Line, CancellationToken ct = default);

    /// <summary>Transitions the record to <paramref name="status"/>, optionally setting the matching stage flag + timestamp.</summary>
    Task<bool> UpdateStatusAsync(long id, MasterRecordStatus status, string? remarks, string? lastError, CancellationToken ct = default);
    Task<bool> IncrementRetryAsync(long id, string? lastError, CancellationToken ct = default);

    /// <summary>
    /// Records a failed attempt on the master row: bumps <c>RetryCount</c>, stores the error and
    /// the failing activity, and (for a retryable activity) schedules the next exponential-backoff
    /// attempt and flags the record for reconciliation once the budget is exhausted.
    /// </summary>
    Task<bool> RecordRetryAsync(long id, int retryCount, string? lastError, string? lastActivity,
        DateTime? nextRetryAt, bool needsReconcile, CancellationToken ct = default);

    /// <summary>Marks the record as requiring manual reconciliation (exhausted retries / CERSAI failure).</summary>
    Task<bool> MarkNeedsReconcileAsync(long id, string reason, CancellationToken ct = default);

    /// <summary>Clears a record's retry bookkeeping (RetryCount/LastError/LastActivity/NextRetryAt/NeedsReconcile) after a successful attempt.</summary>
    Task<bool> ClearRetryStateAsync(long id, CancellationToken ct = default);

    /// <summary>Returns retryable-eligible records for an activity whose next attempt is due (backoff elapsed, budget remaining).</summary>
    Task<IReadOnlyList<MasterRecord>> GetRetryableForActivityAsync(string activityCode, int maxAttempts,
        DateTime now, int limit, CancellationToken ct = default);

    /// <summary>Returns records that need manual intervention/reconciliation (optionally filtered by kind: retry | cersai).</summary>
    Task<IReadOnlyList<MasterRecord>> GetNeedsReconcileAsync(string? kind, int limit, CancellationToken ct = default);

    Task<int> MarkBatchAsync(IReadOnlyCollection<long> ids, string batchFile, IReadOnlyDictionary<long, int>? lineByRecord, CancellationToken ct = default);
    Task<int> CountByStatusAsync(MasterRecordStatus status, CancellationToken ct = default);

    /// <summary>Persists one CERSAI response detail and mirrors it onto the master record summary columns.</summary>
    Task<MasterRecordResponse> AddResponseAsync(MasterRecordResponse response, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecordResponse>> GetResponsesAsync(long masterRecordId, CancellationToken ct = default);

    /// <summary>Persists one stage attempt / retry audit row.</summary>
    Task<int> LogAttemptAsync(MasterRecordAttempt attempt, CancellationToken ct = default);

    // ---- activity-type master ----
    Task<IReadOnlyList<ActivityType>> GetActivityTypesAsync(CancellationToken ct = default);
    Task<ActivityType?> GetActivityTypeByCodeAsync(string code, CancellationToken ct = default);

    // ---- status master ----
    Task<IReadOnlyList<StatusMaster>> GetStatusMastersAsync(CancellationToken ct = default);
    Task<StatusMaster?> GetStatusMasterByValueAsync(int statusValue, CancellationToken ct = default);

    // ---- re-push (reattempt) ----
    /// <summary>Logs a re-push (reattempt) row, snapshotting the previous attempt/response state before the record is reset.</summary>
    Task<MasterRecordReattempt> LogReattemptAsync(MasterRecordReattempt reattempt, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecordReattempt>> GetReattemptsAsync(long masterRecordId, CancellationToken ct = default);

    /// <summary>Resets a record's flags/stage so a fixed, previously-rejected record can be re-pushed through the batch flow.</summary>
    Task<bool> ResetForReattemptAsync(long id, string remarks, CancellationToken ct = default);
}

/// <summary>Individual record tables operations (step 3 persistence).</summary>
public interface IIndividualRepository
{
    Task<SaveRecordResult> SaveAsync(Individual record, CancellationToken ct = default);
    Task<IReadOnlyList<Individual>> GetBySourceCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default);
}

/// <summary>Legal-entity record tables operations (step 3 persistence, client type L).</summary>
public interface ILegalEntityRepository
{
    Task<SaveRecordResult> SaveAsync(LegalEntity record, CancellationToken ct = default);
    Task<IReadOnlyList<LegalEntity>> GetBySourceCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default);
}

/// <summary>Dummy CRM API client (step 2).</summary>
public interface ICrmApiClient
{
    Task<IReadOnlyList<string>> GetCustomerIdsAsync(CancellationToken ct = default);
    Task<Individual?> GetCustomerAsync(string customerId, CancellationToken ct = default);
}

/// <summary>Builds the pipe-delimited .UPL file and its zip archive (step 4).</summary>
public interface IBatchGenerator
{
    Task<GeneratedBatch> GenerateAsync(IReadOnlyList<Individual> records, DateOnly businessDate, CancellationToken ct = default);
}

/// <summary>Builds the pipe-delimited .UPL file and its zip archive for legal entities (step 4, client type L).</summary>
public interface ILegalEntityBatchGenerator
{
    Task<GeneratedBatch> GenerateAsync(IReadOnlyList<LegalEntity> records, DateOnly businessDate, CancellationToken ct = default);
}

/// <summary>Invokes the FVU over a generated batch and returns the processed output (step 5).</summary>
public interface IFvuRunner
{
    Task<FvuRunResult> RunAsync(GeneratedBatch batch, CancellationToken ct = default);
}

/// <summary>File hashing used for the final hash value.</summary>
public interface IFileHasher
{
    string ComputeSha256(string filePath);
    string ComputeSha256(byte[] bytes);
}

/// <summary>Audit trail of generated batches and FVU runs.</summary>
public interface IBatchJournal
{
    Task LogBatchAsync(GeneratedBatch batch, CancellationToken ct = default);
    Task LogFvuRunAsync(FvuRunResult result, CancellationToken ct = default);
    Task<GeneratedBatch?> GetLastBatchAsync(CancellationToken ct = default);
}
