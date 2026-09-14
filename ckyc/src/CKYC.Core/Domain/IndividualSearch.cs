namespace CKYC.Core.Domain;

/// <summary>
/// Processing state of one <see cref="IndividualSearch"/> row:
/// 0 Pending, 1 Processing (claimed), 2 Completed, 3 Failed (retryable).
/// </summary>
public enum IndividualSearchProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// Outcome of a customer search call. <see cref="Found"/> means an existing CKYC record
/// was matched (a CKYC reference number came back); <see cref="NotFound"/> means the API
/// returned a search key to use when creating the new record; <see cref="Skipped"/> means
/// the row was not searched because an earlier row for the same customer already matched.
/// </summary>
public enum IndividualSearchOutcome
{
    NotFound = 0,
    Found = 1,
    Error = 2,
    Skipped = 3,
}

/// <summary>
/// One candidate customer-search request for an individual master record, built from the
/// details actually held for that customer (a pay-off row per identity document held, plus
/// a name/DOB/gender/relation fallback). A customer can therefore have many rows — the
/// number depends entirely on how much data was enriched from the CRM. Each row carries
/// both the request fields (the individual-format-search record-20 layout) and the result
/// returned by the search API.
/// </summary>
public sealed class IndividualSearch
{
    public long Id { get; set; }
    public long MasterRecordId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string ClientType { get; set; } = "I";

    // ---- request (record-20 search layout) ----
    public int SearchOption { get; set; }
    public string? IdentityTypeAndNumber { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? DateOfBirth { get; set; }               // DD-MM-YYYY
    public string? LegalEntityName { get; set; }
    public string? DateOfIncorporation { get; set; }
    public string? Gender { get; set; }                    // M / F / T
    public string? PhotoReferenceNumber { get; set; }
    public string? Relation { get; set; }                  // Father / Mother / Spouse
    public string? RelationFirstName { get; set; }
    public string? RelationMiddleName { get; set; }
    public string? RelationLastName { get; set; }
    public string? MobileNumber { get; set; }
    public string? VerifiableCredential { get; set; }
    public string? Constitution { get; set; }
    public string? RawRequestJson { get; set; }

    // ---- processing ----
    public IndividualSearchProcessingStatus ProcessingStatus { get; set; } = IndividualSearchProcessingStatus.Pending;
    public string? ClaimToken { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // ---- result ----
    public IndividualSearchOutcome? Outcome { get; set; }
    public string? SearchKey { get; set; }                 // 20-char key returned when not found
    public string? CkycReferenceNumber { get; set; }       // present when found
    public string? ResponseRemark { get; set; }
    public DateTime? ResponseReadAt { get; set; }
    public string? RawResponseJson { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A batch of <see cref="IndividualSearch"/> rows claimed by one worker.</summary>
public sealed record IndividualSearchClaim(string Token, IReadOnlyList<IndividualSearch> Rows);

/// <summary>The request sent to the customer-search API (the search record-20 layout).</summary>
public sealed record IndividualSearchApiRequest(
    string CustomerId,
    string ClientType,
    int SearchOption,
    string? IdentityTypeAndNumber,
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string? DateOfBirth,
    string? Gender,
    string? PhotoReferenceNumber,
    string? Relation,
    string? RelationFirstName,
    string? RelationMiddleName,
    string? RelationLastName,
    string? MobileNumber,
    string? VerifiableCredential,
    string? LegalEntityName = null,
    string? DateOfIncorporation = null,
    string? Constitution = null);

/// <summary>The result of one customer-search call.</summary>
public sealed record IndividualSearchApiResult(
    IndividualSearchOutcome Outcome,
    string? SearchKey,
    string? CkycReferenceNumber,
    string? Remark,
    string? RawResponseJson,
    string? Error = null)
{
    public static IndividualSearchApiResult Found(string ckycReference, string? remark, string? raw = null)
        => new(IndividualSearchOutcome.Found, null, ckycReference, remark, raw);

    public static IndividualSearchApiResult NotFound(string searchKey, string? remark, string? raw = null)
        => new(IndividualSearchOutcome.NotFound, searchKey, null, remark, raw);

    public static IndividualSearchApiResult Failed(string error)
        => new(IndividualSearchOutcome.Error, null, null, null, null, error);
}

public static class IndividualSearchOutcomeCodes
{
    public const string Found = "Found";
    public const string NotFound = "NotFound";
    public const string Error = "Error";
    public const string Skipped = "Skipped";

    public static string For(IndividualSearchOutcome outcome) => outcome switch
    {
        IndividualSearchOutcome.Found => Found,
        IndividualSearchOutcome.NotFound => NotFound,
        IndividualSearchOutcome.Error => Error,
        IndividualSearchOutcome.Skipped => Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown search outcome."),
    };

    public static IndividualSearchOutcome Parse(string? value) => value?.Trim() switch
    {
        Found => IndividualSearchOutcome.Found,
        NotFound => IndividualSearchOutcome.NotFound,
        Error => IndividualSearchOutcome.Error,
        Skipped => IndividualSearchOutcome.Skipped,
        _ => IndividualSearchOutcome.NotFound,
    };
}
