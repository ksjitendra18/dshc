using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class MasterRecordAttempt
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public string? Stage { get; set; }

    public long? ActivityTypeId { get; set; }

    public int? Attempt { get; set; }

    public int? Status { get; set; }

    public int? Success { get; set; }

    public string? Error { get; set; }

    public string? Remarks { get; set; }

    public DateTime? AttemptedAt { get; set; }

    public DateTime? NextRetryAt { get; set; }

    public DateTime? CreatedAt { get; set; }
}
