using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class UpdateRequest
{
    public long Id { get; set; }

    public string? ExternalRequestId { get; set; }

    public string? CustomerId { get; set; }

    public string? ClientType { get; set; }

    public string? CkycNumber { get; set; }

    public int? ProcessingStatus { get; set; }

    public string? ClaimToken { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? OutputFileName { get; set; }

    public int? OutputLineNumber { get; set; }

    public string? OutputBatchKey { get; set; }

    public string? ResponseStatus { get; set; }

    public string? LastAckNumber { get; set; }

    public string? LastResponseStatusCode { get; set; }

    public string? LastResponseRemark { get; set; }

    public DateTime? ResponseReadAt { get; set; }

    public string? LastError { get; set; }

    public string? RawRequestJson { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
