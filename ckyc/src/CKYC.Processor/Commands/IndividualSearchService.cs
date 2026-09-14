using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CKYC.Core.Domain;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// The pre-batch customer search. For each individual master record it builds the candidate
/// search rows from the details actually held for that customer (one record-20 option-1 row
/// per identity document + a name/DOB/gender/relation option-2 fallback), calls the search
/// API for each until one matches, and then:
/// <list type="bullet">
///   <item>a match exists -> the record becomes <see cref="MasterRecordStatus.SearchFound"/>
///        (terminal — the CKYC record already exists, so it is no longer pushed through creation);</item>
///   <item>no match -> the 20-character search key returned by the API is written into
///        record 20 and the record becomes <see cref="MasterRecordStatus.Searched"/>, ready to batch.</item>
/// </list>
/// A failed call is recorded as a retryable <c>Search</c> activity, reusing the standard
/// exponential-backoff/reconciliation bookkeeping.
/// </summary>
public sealed class IndividualSearchService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Individual search identity types allowed by the search format (A–G, plus Z = CKYC number).</summary>
    private static readonly HashSet<string> AllowedIdentityTypes =
        new(["A", "B", "C", "D", "E", "F", "G", "Z"], StringComparer.Ordinal);

    private readonly AppContext _ctx;

    public IndividualSearchService(AppContext ctx) => _ctx = ctx;

    /// <summary>
    /// Runs the search step for one PendingSearch individual record. Returns the status the
    /// record was left in: <see cref="MasterRecordStatus.SearchFound"/>,
    /// <see cref="MasterRecordStatus.Searched"/>, or <see cref="MasterRecordStatus.Failed"/>.
    /// </summary>
    public async Task<MasterRecordStatus> ProcessAsync(MasterRecord record, CancellationToken ct = default)
    {
        // Supported skip: searchApi.enabled=false turns the whole step into a pass-through.
        if (!_ctx.Settings.SearchApi.Enabled)
            return await SkipAsync(record, ct);

        var individuals = await _ctx.Individuals.GetByCustomerIdsAsync(new[] { record.CustomerId }, ct);
        var individual = individuals.Count > 0 ? individuals[0] : null;
        if (individual is null)
        {
            await FailMasterAsync(record, "No stored individual record found to search", ct);
            return MasterRecordStatus.Failed;
        }

        if (!await _ctx.IndividualSearches.ExistsForMasterRecordAsync(record.Id, ct))
        {
            var built = BuildRows(record, individual);
            if (built.Count == 0)
            {
                await FailMasterAsync(record, "No searchable identity or name details available", ct);
                return MasterRecordStatus.Failed;
            }
            await _ctx.IndividualSearches.InsertAsync(built, ct);
            Log.Info("[search-customer] [{CustomerId}] prepared {Count} search row(s): {Rows}",
                record.CustomerId, built.Count, string.Join(", ", built.Select(Describe)));
        }

        var rows = await _ctx.IndividualSearches.GetByMasterRecordAsync(record.Id, ct);
        var errors = new List<string>();
        var matchedNow = false;

        foreach (var row in rows)
        {
            if (row.ProcessingStatus == IndividualSearchProcessingStatus.Completed) continue;

            IndividualSearchApiResult result;
            try
            {
                result = await _ctx.SearchApi.SearchAsync(ToApiRequest(row), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = IndividualSearchApiResult.Failed(ex.Message);
            }

            if (result.Outcome == IndividualSearchOutcome.Error)
            {
                await _ctx.IndividualSearches.FailAsync(row, result.Error ?? "Search call failed", ct);
                errors.Add($"{Describe(row)}: {result.Error}");
                continue;
            }

            await _ctx.IndividualSearches.CompleteAsync(row, result, ct);
            if (result.Outcome == IndividualSearchOutcome.Found)
            {
                matchedNow = true;
                break;
            }
        }

        // Derive the outcome from the persisted rows so a partial run or a manual re-run of
        // an already-completed record resolves correctly instead of failing.
        var refreshed = await _ctx.IndividualSearches.GetByMasterRecordAsync(record.Id, ct);
        IndividualSearch? matched = null;
        IndividualSearch? notFound = null;
        foreach (var row in refreshed)
        {
            if (matched is null && row.Outcome == IndividualSearchOutcome.Found) matched = row;
            if (notFound is null && row.Outcome == IndividualSearchOutcome.NotFound) notFound = row;
        }

        if (matched is not null)
        {
            if (matchedNow)
            {
                foreach (var rest in refreshed)
                    if (rest.ProcessingStatus != IndividualSearchProcessingStatus.Completed)
                        await _ctx.IndividualSearches.SkipAsync(rest, "Skipped: an earlier search row already matched", ct);
            }

            await _ctx.Master.MarkSearchFoundAsync(record.Id, matched.CkycReferenceNumber ?? string.Empty, matched.ResponseRemark, ct);
            await _ctx.Master.ClearRetryStateAsync(record.Id, ct);
            await LogAttemptAsync(record, MasterRecordStatus.SearchFound, true, null,
                $"{Describe(matched)} matched CKYC reference {matched.CkycReferenceNumber}", ct);
            Log.Info("[search-customer] [{CustomerId}] MATCHED via {Row}: CKYC reference {Reference} -> journey ends (SearchFound)",
                record.CustomerId, Describe(matched), matched.CkycReferenceNumber);
            return MasterRecordStatus.SearchFound;
        }

        if (notFound is not null)
        {
            var searchKey = notFound.SearchKey;
            if (!string.IsNullOrWhiteSpace(searchKey))
                await _ctx.Individuals.UpdateSearchKeyAsync(record.Id, searchKey, ct);

            var remarks = string.IsNullOrWhiteSpace(searchKey)
                ? "Search not found; API returned no search key"
                : $"Search not found; search key {searchKey} written to record 20";
            await _ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.Searched, remarks, null, ct);
            await _ctx.Master.ClearRetryStateAsync(record.Id, ct);
            await LogAttemptAsync(record, MasterRecordStatus.Searched, true, null, remarks, ct);
            Log.Info("[search-customer] [{CustomerId}] not found -> key '{SearchKey}' written to record 20 (Searched)",
                record.CustomerId, searchKey);
            return MasterRecordStatus.Searched;
        }

        await FailMasterAsync(record, "All customer search attempts failed: " + string.Join("; ", errors), ct);
        return MasterRecordStatus.Failed;
    }

    /// <summary>
    /// Pass-through used when the search is disabled (<c>searchApi.enabled=false</c>): no API
    /// call, no <c>individual_search</c> rows — the record simply becomes Searched so the
    /// normal batching continues. A record-20 search key is retained if present and generated
    /// deterministically if it is missing, so the record still passes build-zip pre-flight.
    /// </summary>
    private async Task<MasterRecordStatus> SkipAsync(MasterRecord record, CancellationToken ct)
    {
        var individuals = await _ctx.Individuals.GetByCustomerIdsAsync(new[] { record.CustomerId }, ct);
        var individual = individuals.Count > 0 ? individuals[0] : null;
        if (individual is not null && individual.SearchKey.Trim().Length != 20)
            await _ctx.Individuals.UpdateSearchKeyAsync(record.Id, NewSearchKey(record.CustomerId), ct);

        const string remarks = "Customer search skipped (searchApi.enabled=false); record-20 search key used as-is";
        await _ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.Searched, remarks, null, ct);
        await _ctx.Master.ClearRetryStateAsync(record.Id, ct);
        await LogAttemptAsync(record, MasterRecordStatus.Searched, true, null, remarks, ct);
        Log.Warn("[search-customer] [{CustomerId}] search DISABLED -> skipped straight to Searched", record.CustomerId);
        return MasterRecordStatus.Searched;
    }

    /// <summary>Deterministic 20-char fallback search key (IMO + 17 digits) for a skipped search.</summary>
    private static string NewSearchKey(string customerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(customerId + "SKIP"));
        var sb = new StringBuilder("IMO", 20);
        for (var i = 0; sb.Length < 20; i++) sb.Append((char)('0' + bytes[i % bytes.Length] % 10));
        return sb.ToString();
    }

    /// <summary>
    /// Builds the candidate search rows for a customer: one option-1 row per identity
    /// document held (record-30 OVDs + record-20 PAN), then a single option-2 fallback row
    /// when a name, DOB, gender and a relation are all present.
    /// </summary>
    public static List<IndividualSearch> BuildRows(MasterRecord record, Individual individual)
    {
        var rows = new List<IndividualSearch>();
        var seenIdentityTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var proof in individual.Proofs)
        {
            var type = proof.OvdType?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(type) || !AllowedIdentityTypes.Contains(type)) continue;
            var number = NormalizeIdentityNumber(type, proof.IdNumber);
            if (number is null || !seenIdentityTypes.Add(type)) continue;
            rows.Add(NewRow(record, individual, 1, $"{type}^{number}"));
        }

        // Record-20 PAN is a searchable identity in its own right (type C) even without a
        // record-30 PAN proof.
        if (!string.IsNullOrWhiteSpace(individual.Pan) && seenIdentityTypes.Add("C"))
            rows.Add(NewRow(record, individual, 1, $"C^{individual.Pan.Trim()}"));

        var (relation, first, middle, last) = ResolveRelation(individual);
        if (individual.Name.HasAnyName
            && !string.IsNullOrWhiteSpace(individual.DateOfBirth)
            && !string.IsNullOrWhiteSpace(individual.Gender)
            && !string.IsNullOrWhiteSpace(first))
        {
            var row = NewRow(record, individual, 2, identityTypeAndNumber: null);
            row.Relation = relation;
            row.RelationFirstName = first;
            row.RelationMiddleName = middle;
            row.RelationLastName = last;
            rows.Add(row);
        }

        foreach (var row in rows)
            row.RawRequestJson = JsonSerializer.Serialize(ToApiRequest(row));

        return rows;
    }

    private static IndividualSearch NewRow(MasterRecord record, Individual individual, int searchOption, string? identityTypeAndNumber)
    {
        var now = DateTime.UtcNow;
        return new IndividualSearch
        {
            MasterRecordId = record.Id,
            CustomerId = individual.CustomerId,
            ClientType = "I",
            SearchOption = searchOption,
            IdentityTypeAndNumber = identityTypeAndNumber,
            FirstName = individual.Name.FirstName,
            MiddleName = individual.Name.MiddleName,
            LastName = individual.Name.LastName,
            DateOfBirth = individual.DateOfBirth,
            Gender = individual.Gender,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Aadhaar (E) searches must carry exactly the last four digits; other identity numbers
    /// are trimmed to the format's allowed characters and length (1–40 alphanumeric).
    /// </summary>
    private static string? NormalizeIdentityNumber(string type, string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber)) return null;
        var digits = new string(idNumber.Where(char.IsLetterOrDigit).ToArray());
        if (digits.Length == 0) return null;

        if (type == "E")
        {
            var lastFour = digits.Length <= 4 ? digits : digits[^4..];
            return lastFour.All(char.IsDigit) ? lastFour : null;
        }

        return digits.Length > 40 ? digits[..40] : digits;
    }

    private static (string Relation, string First, string? Middle, string? Last) ResolveRelation(Individual individual)
    {
        if (individual.FatherName.HasAnyName)
            return ("Father", individual.FatherName.FirstName, individual.FatherName.MiddleName, individual.FatherName.LastName);
        if (individual.MotherName.HasAnyName)
            return ("Mother", individual.MotherName.FirstName, individual.MotherName.MiddleName, individual.MotherName.LastName);
        if (individual.SpouseName.HasAnyName)
            return ("Spouse", individual.SpouseName.FirstName, individual.SpouseName.MiddleName, individual.SpouseName.LastName);
        return (string.Empty, string.Empty, null, null);
    }

    private static IndividualSearchApiRequest ToApiRequest(IndividualSearch row) => new(
        row.CustomerId, row.ClientType, row.SearchOption, row.IdentityTypeAndNumber,
        row.FirstName, row.MiddleName, row.LastName, row.DateOfBirth, row.Gender,
        row.PhotoReferenceNumber, row.Relation, row.RelationFirstName, row.RelationMiddleName,
        row.RelationLastName, row.MobileNumber, row.VerifiableCredential,
        row.LegalEntityName, row.DateOfIncorporation, row.Constitution);

    private static string Describe(IndividualSearch row)
        => row.SearchOption == 1
            ? $"option 1 ({row.IdentityTypeAndNumber})"
            : $"option {row.SearchOption} (name+DOB)";

    private async Task FailMasterAsync(MasterRecord record, string error, CancellationToken ct)
    {
        var activity = await _ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.Search, ct);
        var attempt = record.RetryCount + 1;
        var retryable = activity is { IsRetryable: true };
        var nextRetryAt = retryable ? DateTime.UtcNow.AddHours(activity!.BackoffHoursAfter(attempt)) : (DateTime?)null;
        var exhausted = retryable && activity!.IsExhausted(attempt);

        await _ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.Failed, null, error, ct);
        await _ctx.Master.RecordRetryAsync(record.Id, attempt, error, ActivityTypeCodes.Search, nextRetryAt, exhausted, ct);
        await LogAttemptAsync(record, MasterRecordStatus.Failed, false, error,
            $"retry {attempt}{(exhausted ? " (budget exhausted -> reconcile)" : "")}", ct, activity?.Id, nextRetryAt);
    }

    private Task<int> LogAttemptAsync(MasterRecord record, MasterRecordStatus status, bool success, string? error,
        string? remarks, CancellationToken ct, long? activityTypeId = null, DateTime? nextRetryAt = null)
        => _ctx.Master.LogAttemptAsync(new MasterRecordAttempt
        {
            MasterRecordId = record.Id,
            CustomerId = record.CustomerId,
            Stage = ActivityTypeCodes.Search,
            ActivityTypeId = activityTypeId,
            Status = (int)status,
            Success = success,
            Error = error,
            Remarks = remarks,
            AttemptedAt = DateTime.UtcNow,
            NextRetryAt = nextRetryAt,
        }, ct);
}
