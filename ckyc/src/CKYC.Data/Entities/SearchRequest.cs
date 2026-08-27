using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class SearchRequest
{
    public long Id { get; set; }

    public string? ExternalRequestId { get; set; }

    public string? CustomerId { get; set; }

    public string? ClientType { get; set; }

    public int? SearchOption { get; set; }

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

    public int? ProcessingStatus { get; set; }

    public string? ClaimToken { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? OutputFileName { get; set; }

    public int? OutputLineNumber { get; set; }

    public string? ResponseStatus { get; set; }

    public string? LastSearchKey { get; set; }

    public string? LastCkycReference { get; set; }

    public string? LastResponseRemark { get; set; }

    public DateTime? ResponseReadAt { get; set; }

    public string? LastError { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
