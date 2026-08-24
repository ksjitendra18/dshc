namespace CKYC.Core.Domain;

/// <summary>
/// Master list of the processes / activities the pipeline can perform, together with the
/// <b>retry policy</b> that governs whether (and how) a failed activity may be re-run.
///
/// Only <em>some</em> activities are retryable — e.g. fetching the daily customer ids from
/// the Core Banking System (CBS) is retryable, whereas reconciliation (a human-in-the-loop
/// step) is not. Retryable activities carry an exponential-backoff schedule: the delay
/// before the next attempt grows by <see cref="BackoffMultiplier"/> each failure, starting
/// from <see cref="BackoffBaseHours"/>, and no record is attempted more than
/// <see cref="MaxAttempts"/> times. Once the budget is exhausted the record is flagged for
/// reconciliation (manual intervention).
///
/// The same table is the <b>audit-trail anchor</b>: every row written to
/// <c>master_record_attempt</c> references its activity (via <c>ActivityTypeId</c>) and
/// records <em>when</em> the attempt was processed and <em>what the outcome</em> was
/// (success/error), so the full history is traceable back to a well-defined activity.
/// </summary>
public sealed class ActivityType
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this activity is eligible for automatic retry at all.</summary>
    public bool IsRetryable { get; set; }

    /// <summary>Maximum number of attempts before the record is pushed to reconciliation.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base delay (in hours) before the 2nd attempt — the start of the backoff ladder.</summary>
    public int BackoffBaseHours { get; set; } = 24;

    /// <summary>Exponential multiplier applied to the base delay per additional failure (2 = double).</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    public bool IsActive { get; set; } = true;
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Hours to wait before the next attempt after attempt <paramref name="failedAttempt"/>
    /// (1-based) has failed. Exponential: base × multiplier^(attempt−1).
    /// </summary>
    public double BackoffHoursAfter(int failedAttempt)
        => BackoffBaseHours * Math.Pow(BackoffMultiplier, Math.Max(0, failedAttempt - 1));

    /// <summary>True once <paramref name="attemptsMade"/> reaches the retry budget (no more auto-retries).</summary>
    public bool IsExhausted(int attemptsMade) => IsRetryable && attemptsMade >= MaxAttempts;
}

public static class ActivityTypeCodes
{
    public const string CbsFetch = "CbsFetch";
    public const string Crm = "Crm";
    public const string Store = "Store";
    public const string BuildZip = "BuildZip";
    public const string FvuUpload = "FvuUpload";
    public const string ResponseRead = "Response";
    public const string Reconciliation = "Reconciliation";
}
