namespace CKYC.Core.Domain;

/// <summary>One record-20 row from a CKYCR individual search request.</summary>
public sealed class SearchRequest
{
    public long Id { get; set; }
    public string? ExternalRequestId { get; set; }
    public string? SourceCustomerId { get; set; }
    public string ClientType { get; set; } = "I";
    public int SearchOption { get; set; }
    public string? IdentityTypeAndNumber { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? DateOfBirth { get; set; }
    public string? LegalEntityName { get; set; }
    public string? DateOfIncorporation { get; set; }
    public string? Gender { get; set; }
    public string? PhotoReferenceNumber { get; set; }
    public string? Relation { get; set; }
    public string? RelationFirstName { get; set; }
    public string? RelationMiddleName { get; set; }
    public string? RelationLastName { get; set; }
    public string? MobileNumber { get; set; }
    public string? VerifiableCredential { get; set; }
    public string? Constitution { get; set; }
    public string? RawRequestJson { get; set; }
    public int ProcessingStatus { get; set; }
    public string? ClaimToken { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public string? OutputFileName { get; set; }
    public int? OutputLineNumber { get; set; }
    public string? LastError { get; set; }
}

public sealed record SearchClaim(string Token, int FileSequence, IReadOnlyList<SearchRequest> Records);

public sealed record SearchIngestResult(int Inserted, int Total);

/// <summary>A generated SRC file awaiting FVU validation.</summary>
public sealed record SearchGeneratedBatch(long Id, string FileName, string FilePath, int RecordCount);
