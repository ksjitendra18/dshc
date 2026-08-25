namespace CKYC.Core.Domain;

/// <summary>
/// One attempt made to move a master record through a pipeline stage. Because any stage
/// (data fetch, save, batch, FVU upload, response read, reconciliation) can fail and be
/// retried, this table is the audit trail of what happened and when. The <see cref="MasterRecord"/>
/// row's <c>RetryCount</c> / <c>LastError</c> / <c>LastAttemptAt</c> / <c>NextRetryAt</c> are the
/// rolled-up "latest attempt" summary; this table keeps the full history.
///
/// Each row is anchored to an <see cref="ActivityType"/> (the activity master) so the audit
/// trail says <em>which</em> process was attempted, <em>when</em> it was processed and
/// <em>what the outcome</em> was — and, when the attempt failed and the activity is retryable,
/// <see cref="NextRetryAt"/> records the exponential-backoff time of the next attempt.
/// </summary>
public sealed class MasterRecordAttempt
{
    public long Id { get; set; }
    public long MasterRecordId { get; set; }
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Stage name of this attempt (Fetch, Crm, Store, BuildZip, FvuUpload, Response, Recon).</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>FK to the activity type master (<see cref="ActivityType"/>).</summary>
    public long? ActivityTypeId { get; set; }

    /// <summary>1-based attempt number for this stage.</summary>
    public int Attempt { get; set; }

    /// <summary>Master status before/at this attempt.</summary>
    public int? Status { get; set; }

    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Remarks { get; set; }
    public DateTime? AttemptedAt { get; set; }

    /// <summary>Exponential-backoff time of the next attempt (only set when this attempt failed and the activity is retryable).</summary>
    public DateTime? NextRetryAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
