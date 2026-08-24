namespace CKYC.Core.Domain;

/// <summary>
/// Row of the master table: the daily incoming source customer id together with the
/// single source of truth for where the record is in the CKYC pipeline.
/// <para>
/// The <see cref="Status"/> value tells you the current stage (e.g. "waiting for batch",
/// "uploaded &amp; pending at CERSAI", "response read", "reconciled"). The per-stage
/// <c>Is*</c> flags and <c>*At</c> timestamps record whether each stage has been reached
/// and when; the <c>LastResponse*</c> summary columns mirror the most recent CERSAI reply
/// detail (full history lives in <see cref="MasterRecordResponse"/>). <c>RetryCount</c> /
/// <c>LastError</c> / <c>LastAttemptAt</c> keep failed stages re-runnable through the
/// <c>retry</c> command.
/// </summary>
public sealed class MasterRecord
{
    public long Id { get; set; }
    public string SourceCustomerId { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public MasterRecordStatus Status { get; set; } = MasterRecordStatus.Pending;
    public string? Remarks { get; set; }

    // ---- retry / attempt bookkeeping (support retries at any stage) ----
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Activity code (<see cref="ActivityType.Code"/>) of the last failed attempt (drives which retry to run).</summary>
    public string? LastActivity { get; set; }

    /// <summary>Exponential-backoff time of the next auto-retry (only set while the activity is retryable and not yet exhausted).</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>True when the record has exhausted its retry budget (or failed at CERSAI) and needs manual intervention / reconciliation.</summary>
    public bool NeedsReconcile { get; set; }

    /// <summary>Number of times this record has been re-pushed after a manual backend fix.</summary>
    public int ReattemptCount { get; set; }

    /// <summary>When this record was last re-pushed.</summary>
    public DateTime? ReattemptedAt { get; set; }

    // ---- batch bookkeeping ----
    public string? BatchFile { get; set; }

    /// <summary>Line number of this record's record-20 within the batch .UPL file (used to map a CERSAI response back to the record).</summary>
    public int? BatchRecordLine { get; set; }

    // ---- stage flags (has the stage been reached?) ----
    public bool IsCrmFetched { get; set; }
    public bool IsSaved { get; set; }
    public bool IsBatched { get; set; }
    public bool IsUploaded { get; set; }
    public bool IsResponseRead { get; set; }
    public bool IsReconciled { get; set; }
    public bool IsRejected { get; set; }

    // ---- stage timestamps ----
    public DateTime? CrmFetchedAt { get; set; }
    public DateTime? SavedAt { get; set; }
    public DateTime? BatchedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ReconciledAt { get; set; }

    // ---- latest CERSAI response summary (mirrors the newest master_record_response row) ----
    public int? LastResponseFileNumber { get; set; }
    public string? LastResponseFileName { get; set; }
    public string? LastResponseAckNumber { get; set; }
    public string? LastResponseStatus { get; set; }
    public string? LastResponseCkycReference { get; set; }
    public string? LastResponseCkycNumber { get; set; }
    public string? LastResponseRejectionRemark { get; set; }
    public DateTime? LastResponseReadAt { get; set; }
    public string? LastResponseRemarks { get; set; }

    // ---- reconciliation ----
    public string? ReconStatus { get; set; }
    public string? ReconRemarks { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsRetryable(int maxRetries) => RetryCount < maxRetries;
}
