using System.Data.Common;
using CKYC.Core.Configuration;
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

    /// <summary>
    /// Upserts the daily source records (customer id + optional document key + intake channel):
    /// inserts new master rows in <c>Pending</c> and returns the insert/skip counts.
    /// </summary>
    Task<FetchResult> UpsertCustomersAsync(IReadOnlyCollection<SourceCustomer> customers, DateOnly businessDate, CancellationToken ct = default);

    Task<IReadOnlyList<MasterRecord>> GetByStatusAsync(MasterRecordStatus status, int limit, string? clientType = null, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecord>> GetRetryableAsync(int maxRetries, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecord>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default);
    Task<MasterRecord?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<MasterRecord> EnsureAsync(string customerId, DateOnly businessDate, string? clientType = null, CancellationToken ct = default);
    Task<IReadOnlyList<MasterRecord>> GetByBatchFileAsync(string batchFile, CancellationToken ct = default);
    Task<MasterRecord?> GetByBatchLineAsync(string batchFile, int record20Line, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerBatchRecord>> GetBatchHistoryAsync(string customerId, CancellationToken ct = default);

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

    /// <summary>Marks a record as <see cref="MasterRecordStatus.SearchFound"/> and stores the matched CKYC reference on the master summary.</summary>
    Task<bool> MarkSearchFoundAsync(long id, string ckycReferenceNumber, string? remarks, CancellationToken ct = default);

    /// <summary>
    /// Marks a record's supporting image/document as fetched: sets the <c>IsImageFetched</c>
    /// flag/timestamp and advances the record to <see cref="MasterRecordStatus.PendingSearch"/>
    /// so the customer search (and only then batching) can proceed.
    /// </summary>
    Task<bool> MarkImageFetchedAsync(long id, string? remarks, CancellationToken ct = default);

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
    Task<bool> HasUploadResponseFileAsync(string sourceHash, string responseFileName, CancellationToken ct = default);
    Task<bool> TryAddUploadResponseFileAsync(UploadResponseFile responseFile, CancellationToken ct = default);

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
    Task<IReadOnlyList<Individual>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default);

    /// <summary>Writes the search key returned by the customer-search API into record 20 for a master record.</summary>
    Task<bool> UpdateSearchKeyAsync(long masterRecordId, string searchKey, CancellationToken ct = default);
}

/// <summary>Legal-entity record tables operations (step 3 persistence, client type L).</summary>
public interface ILegalEntityRepository
{
    Task<SaveRecordResult> SaveAsync(LegalEntity record, CancellationToken ct = default);
    Task<IReadOnlyList<LegalEntity>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default);
}

/// <summary>Persists and retrieves customer supporting documents independently of their source.</summary>
public interface IDocumentStore
{
    Task<CustomerDocument> ImportAsync(DocumentImport import, Stream content, CancellationToken ct = default);
    Task<CustomerDocument?> GetAsync(long masterRecordId, string fileName, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerDocument>> GetByMasterRecordIdsAsync(IReadOnlyCollection<long> masterRecordIds, CancellationToken ct = default);
}

/// <summary>Identifies the image/document to fetch for one customer (channel + step-1 document key).</summary>
public sealed record DocumentFetchRequest(string Channel, string CustomerId, string DocumentKey);

/// <summary>A document retrieved from an external source, ready to import into the store.</summary>
public sealed record FetchedDocument(string RemoteName, byte[] Content, string SourceReference);

/// <summary>
/// Channel-specific image/document source. One implementation per intake channel
/// (e.g. the beckyc SFTP folder), selected by <see cref="Channel"/>.
/// </summary>
public interface IDocumentSource
{
    /// <summary>The intake channel this source serves (<c>app</c>, <c>beckyc</c>, …).</summary>
    string Channel { get; }

    /// <summary>True when this source is fully configured and can fetch.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Fetches the configured image/document(s) for the request. Throws when the source is
    /// unreachable or the expected file is missing, so the caller can block + retry the record.
    /// </summary>
    Task<IReadOnlyList<FetchedDocument>> FetchAsync(DocumentFetchRequest request, CancellationToken ct = default);
}

/// <summary>Resolves the configured <see cref="IDocumentSource"/> for an intake channel.</summary>
public interface IDocumentSourceRegistry
{
    /// <summary>True when the channel has an active source configured.</summary>
    bool IsConfigured(string? channel);

    /// <summary>The source for a channel, or null when none is configured.</summary>
    IDocumentSource? Resolve(string? channel);
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

    /// <summary>
    /// Same as <see cref="GenerateAsync(IReadOnlyList{Individual}, DateOnly, CancellationToken)"/> but overlays
    /// batch-time <paramref name="generatedDocuments"/> onto the supporting documents referenced by the records.
    /// </summary>
    Task<GeneratedBatch> GenerateAsync(IReadOnlyList<Individual> records, DateOnly businessDate,
        IReadOnlyList<GeneratedDocument>? generatedDocuments, CancellationToken ct = default);
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

/// <summary>
/// Transport over the CERSAI SFTP utility: push FVU-validated batches and pull processed
/// response files. Implemented by the <c>CKYC.Sftp</c> project.
/// </summary>
public interface ISftpRunner
{
    /// <summary>The resolved folders the runner reads/writes (shared with the FVU output routing).</summary>
    SftpPaths Paths { get; }

    /// <summary>Runs <c>SFTPRunner.exe upload</c> over the configured individual/legal scan folders.</summary>
    Task<SftpRunResult> UploadAsync(CancellationToken ct = default);

    /// <summary>Runs <c>SFTPRunner.exe download</c> into the configured download folder.</summary>
    Task<SftpRunResult> DownloadAsync(CancellationToken ct = default);
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
    Task<GeneratedBatch?> GetBatchByKeyAsync(string batchKey, CancellationToken ct = default);
    Task<GeneratedBatch?> GetBatchByUploadFileAsync(string uploadFileName, CancellationToken ct = default);
}
