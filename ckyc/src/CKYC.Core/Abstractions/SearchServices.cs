using CKYC.Core.Domain;

namespace CKYC.Core.Abstractions;

public interface ISearchRepository
{
    Task<SearchIngestResult> InsertAsync(IReadOnlyList<SearchRequest> requests, CancellationToken ct = default);
    Task<SearchClaim?> ClaimAsync(int limit, DateOnly businessDate, int sequenceStart,
        TimeSpan claimTimeout, CancellationToken ct = default);
    Task CompleteAsync(SearchClaim claim, string fileName, string filePath, CancellationToken ct = default);
    Task FailAsync(SearchClaim claim, string failureMessage, CancellationToken ct = default);
    Task<SearchGeneratedBatch?> GetGeneratedBatchAsync(string? fileName, CancellationToken ct = default);
    Task RecordFvuAsync(long batchId, bool passed, string? zipPath, string? hash, string? failureMessage, CancellationToken ct = default);
    Task<SearchResponseImportResult> ImportResponseAsync(SearchResponseImport response, CancellationToken ct = default);
}

/// <summary>Stores immutable CKYCR download response snapshots and their artifacts.</summary>
public interface IDownloadRepository
{
    Task<DownloadImportResult> ImportAsync(DownloadResponseImport response, CancellationToken ct = default);
}

public interface ISearchFileWriter
{
    string Write(IReadOnlyList<SearchRequest> records, DateOnly businessDate);
    string BuildFileName(DateOnly businessDate, int sequence);
}
