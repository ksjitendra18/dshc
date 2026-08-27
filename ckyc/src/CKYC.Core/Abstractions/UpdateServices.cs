using CKYC.Core.Domain;

namespace CKYC.Core.Abstractions;

/// <summary>Persists bulk-update (.UPD) requests and claims per-client-type batches transactionally.</summary>
public interface IUpdateRepository
{
    Task<UpdateIngestResult> InsertAsync(IReadOnlyList<UpdateRequest> requests, CancellationToken ct = default);
    Task<UpdateClaim?> ClaimAsync(string clientType, int limit, DateOnly businessDate, int sequenceStart,
        TimeSpan claimTimeout, CancellationToken ct = default);
    /// <summary>Marks claimed requests processed with their record-20 line numbers inside the .UPD file.</summary>
    Task CompleteAsync(UpdateClaim claim, string batchKey, string fileName, string filePath,
        IReadOnlyDictionary<string, int> lineByCkycNumber, CancellationToken ct = default);
    /// <summary>Permanently fails individual claimed requests (e.g. missing supporting documents)
    /// while keeping the rest of the claim intact.</summary>
    Task SkipAsync(string claimToken, IReadOnlyDictionary<long, string> errorsByRequestId, CancellationToken ct = default);
    Task FailAsync(UpdateClaim claim, string failureMessage, CancellationToken ct = default);
    Task<UpdateGeneratedBatch?> GetGeneratedBatchAsync(string? fileName, CancellationToken ct = default);
    Task RecordFvuAsync(long batchId, bool passed, string? zipPath, string? hash, string? failureMessage, CancellationToken ct = default);
    Task<UpdateResponseImportResult> ImportResponseAsync(UpdateResponseImport response, CancellationToken ct = default);
}

/// <summary>Writes the pipe-delimited .UPD payload for one client type (individual or legal entity).</summary>
public interface IUpdateFileWriter
{
    string ClientType { get; }
    /// <summary>Renders the header (record 10) plus all detail lines for the supplied updates.</summary>
    string Write(IReadOnlyList<UpdateRequest> records, DateOnly businessDate);
    /// <summary>Maps each amended CKYC number to the "20" line number written in the current file.</summary>
    IReadOnlyDictionary<string, int> ComputeRecord20Lines(IReadOnlyList<UpdateRequest> records);
    /// <summary>Distinct support_docs file names referenced by document-typed fields of a request.</summary>
    IReadOnlyCollection<string> ReferencedDocuments(UpdateRequest record);
    /// <summary>The format keys of document-typed positions carrying values on this request's emitted lines.</summary>
    IReadOnlyCollection<string> ReferencedDocumentFieldKeys(UpdateRequest record);
}
