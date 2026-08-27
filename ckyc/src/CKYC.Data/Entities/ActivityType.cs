using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class ActivityType
{
    public long Id { get; set; }

    public string? Code { get; set; }

    public string? Name { get; set; }

    public int? IsRetryable { get; set; }

    public int? MaxAttempts { get; set; }

    public int? BackoffBaseHours { get; set; }

    public double? BackoffMultiplier { get; set; }

    public int? IsActive { get; set; }

    public string? Remarks { get; set; }

    public DateTime? CreatedAt { get; set; }
}
