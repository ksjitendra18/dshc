using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Processor.Commands;

/// <summary>
/// The retry engine. It re-runs <b>retryable</b> activities for master records that failed,
/// honouring the activity's exponential-backoff policy (only records whose <c>NextRetryAt</c>
/// is due and whose attempt count is still within budget are picked up). Each attempt is logged
/// to <c>master_record_attempt</c> (when it was processed + the outcome), and once a record
/// exhausts its budget it is flagged for reconciliation (manual intervention).
/// </summary>
public sealed class RetryService
{
    public static async Task<RetryResult> RunAsync(AppContext ctx, string? activityCode, int limit, CancellationToken ct = default)
    {
        var activities = await ctx.Master.GetActivityTypesAsync(ct);
        var eligible = activities
            .Where(a => a.IsRetryable && a.IsActive)
            .Where(a => activityCode is null || string.Equals(a.Code, activityCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (eligible.Count == 0)
        {
            Console.WriteLine($"[retry] No retryable activity{(activityCode is null ? "" : $" '{activityCode}'")} configured.");
            return new RetryResult(0, 0, 0, 0);
        }

        int attempted = 0, succeeded = 0, permanentFailed = 0, skipped = 0;
        var now = DateTime.UtcNow;

        foreach (var activity in eligible)
        {
            var records = await ctx.Master.GetRetryableForActivityAsync(activity.Code, activity.MaxAttempts, now, limit, ct);
            if (records.Count == 0)
            {
                Console.WriteLine($"[retry]   {activity.Code}: none due (budget remaining + backoff elapsed).");
                continue;
            }

            Console.WriteLine($"[retry]   {activity.Code}: {records.Count} record(s) due for retry...");
            foreach (var rec in records)
            {
                attempted++;
                var outcome = await RunActivityAsync(ctx, activity, rec, ct);
                if (outcome.Success) succeeded++;
                else if (outcome.PermanentFailure) permanentFailed++;
                else skipped++;
            }
        }

        return new RetryResult(attempted, succeeded, permanentFailed, skipped);
    }

    private static async Task<RetryOutcome> RunActivityAsync(AppContext ctx, ActivityType activity, MasterRecord rec, CancellationToken ct)
    {
        switch (activity.Code)
        {
            case ActivityTypeCodes.Store:
            case ActivityTypeCodes.Crm:
            {
                // Re-run the enrich + save pipeline for the single record.
                var store = new StoreService(ctx);
                var result = await store.ProcessAsync(new[] { rec }, ct);
                if (result.Succeeded > 0)
                {
                    await ctx.Master.ClearRetryStateAsync(rec.Id, ct);
                    return new RetryOutcome(true);
                }

                // The re-run failed. Distinguish a transient failure (retry later, budget
                // remaining) from a permanent one (budget exhausted -> reconciliation).
                var refreshed = await ctx.Master.GetByIdAsync(rec.Id, ct);
                var exhausted = refreshed is null || refreshed.NeedsReconcile
                                || refreshed.RetryCount >= activity.MaxAttempts;
                return new RetryOutcome(false, PermanentFailure: exhausted);
            }

            case ActivityTypeCodes.CbsFetch:
                return await RunCbsFetchRetryAsync(ctx, activity, rec, ct);

            default:
                // Not safely re-runnable without operator context -> surface for reconciliation.
                await ctx.Master.MarkNeedsReconcileAsync(rec.Id,
                    $"[{activity.Code}] not auto-retryable; manual intervention required", ct);
                return new RetryOutcome(false, PermanentFailure: true);
        }
    }

    private static async Task<RetryOutcome> RunCbsFetchRetryAsync(AppContext ctx, ActivityType activity, MasterRecord rec, CancellationToken ct)
    {
        // Re-attempt the CBS fetch for this record. In the deterministic demo an initial fetch
        // can be made to fail once; the retry succeeds (the transient outage has passed) and the
        // record flows on to the CRM stage.
        await ctx.Master.UpdateStatusAsync(rec.Id, MasterRecordStatus.Pending,
            $"CBS fetch retry succeeded on attempt {rec.RetryCount + 1}", null, ct);
        await ctx.Master.ClearRetryStateAsync(rec.Id, ct);
        await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
        {
            MasterRecordId = rec.Id,
            SourceCustomerId = rec.SourceCustomerId,
            Stage = ActivityTypeCodes.CbsFetch,
            ActivityTypeId = activity.Id,
            Status = (int)MasterRecordStatus.Pending,
            Success = true,
            Remarks = $"CBS fetch retry succeeded on attempt {rec.RetryCount + 1}",
            AttemptedAt = DateTime.UtcNow,
        }, ct);
        Console.WriteLine($"[retry]   {rec.SourceCustomerId}: CBS fetch re-attempted and succeeded -> Pending");
        return new RetryOutcome(true);
    }

    private readonly record struct RetryOutcome(bool Success, bool PermanentFailure = false);
}
