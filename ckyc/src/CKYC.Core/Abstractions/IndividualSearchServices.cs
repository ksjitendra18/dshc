using CKYC.Core.Domain;

namespace CKYC.Core.Abstractions;

/// <summary>
/// Persists the per-customer search rows (<c>individual_search</c>) and their outcomes.
/// A customer can have many rows — one per identity detail held plus a name-level fallback.
/// </summary>
public interface IIndividualSearchRepository
{
    /// <summary>Inserts the candidate search rows for a customer (idempotent per master record).</summary>
    Task<int> InsertAsync(IReadOnlyList<IndividualSearch> rows, CancellationToken ct = default);

    /// <summary>Returns every search row for a master record (request fields + result).</summary>
    Task<IReadOnlyList<IndividualSearch>> GetByMasterRecordAsync(long masterRecordId, CancellationToken ct = default);

    /// <summary>True when the master record already has at least one search row.</summary>
    Task<bool> ExistsForMasterRecordAsync(long masterRecordId, CancellationToken ct = default);

    /// <summary>Records a completed search call (found / not found / error) on one row.</summary>
    Task CompleteAsync(IndividualSearch row, IndividualSearchApiResult result, CancellationToken ct = default);

    /// <summary>Marks one row Failed with an error so a later retry can re-run it.</summary>
    Task FailAsync(IndividualSearch row, string failureMessage, CancellationToken ct = default);

    /// <summary>Marks one row Skipped because an earlier row for the same customer already matched.</summary>
    Task SkipAsync(IndividualSearch row, string reason, CancellationToken ct = default);
}

/// <summary>
/// Customer-search API client. Mirrors the CRM wiring: a configurable HTTP client for the
/// real endpoint and a deterministic in-process simulator for local/dev runs.
/// </summary>
public interface IIndividualSearchApiClient
{
    Task<IndividualSearchApiResult> SearchAsync(IndividualSearchApiRequest request, CancellationToken ct = default);
}
