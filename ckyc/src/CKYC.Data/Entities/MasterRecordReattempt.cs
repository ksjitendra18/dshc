using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class MasterRecordReattempt
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public string? Reason { get; set; }

    public int? PreviousStatus { get; set; }

    public string? PreviousReconStatus { get; set; }

    public string? PreviousResponseStatus { get; set; }

    public string? PreviousResponseAckNumber { get; set; }

    public string? PreviousResponseCkycReference { get; set; }

    public string? PreviousResponseCkycNumber { get; set; }

    public string? PreviousResponseRejectionRemark { get; set; }

    public DateTime? PreviousResponseReadAt { get; set; }

    public int? PreviousRetryCount { get; set; }

    public int? ReattemptCount { get; set; }

    public DateTime? ReattemptedAt { get; set; }

    public DateTime? CreatedAt { get; set; }
}
