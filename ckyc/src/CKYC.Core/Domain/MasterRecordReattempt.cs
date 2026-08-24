namespace CKYC.Core.Domain;

/// <summary>
/// One <b>re-push (reattempt)</b> of a record that was previously rejected by CERSAI due to a
/// (usually minor) data issue. The record was fixed directly in the backend database, and the
/// reattempt processor re-submits it.
///
/// This row captures the <b>before</b> state — the response/attempt history that is about to be
/// reset — so the "previous attempt" (its outcome, ack, rejection remark and the exact
/// date/timestamp it was received) is never lost when the record's flag is flipped back to a
/// re-pushable stage.
/// </summary>
public sealed class MasterRecordReattempt
{
    public long Id { get; set; }
    public long MasterRecordId { get; set; }
    public string SourceCustomerId { get; set; } = string.Empty;

    /// <summary>Free-text reason the record is being re-pushed (e.g. "PAN corrected in backend").</summary>
    public string? Reason { get; set; }

    // ---- snapshot of the "before" state (what the previous attempt/response told us) ----
    public int? PreviousStatus { get; set; }
    public string? PreviousReconStatus { get; set; }
    public string? PreviousResponseStatus { get; set; }
    public string? PreviousResponseAckNumber { get; set; }
    public string? PreviousResponseCkycReference { get; set; }
    public string? PreviousResponseCkycNumber { get; set; }
    public string? PreviousResponseRejectionRemark { get; set; }
    public DateTime? PreviousResponseReadAt { get; set; }
    public int? PreviousRetryCount { get; set; }

    /// <summary>1-based number of times this record has been re-pushed.</summary>
    public int ReattemptCount { get; set; }

    /// <summary>When the re-push happened.</summary>
    public DateTime? ReattemptedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
