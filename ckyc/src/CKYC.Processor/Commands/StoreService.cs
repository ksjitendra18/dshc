using CKYC.Core.Domain;
using CKYC.Core.Models;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Shared "enrich + save" pipeline for step 3. For each master record it fetches the
/// CRM record, saves the individual details into the record tables, and — to demonstrate
/// the error-saving scenario — deliberately fails a deterministic subset of saves so the
/// retry path can be exercised. Every CRM fetch / save attempt is also recorded in the
/// <c>master_record_attempt</c> audit table (anchored to its <see cref="ActivityType"/>),
/// the master row's stage flags + timestamps are advanced as each stage is reached, and
/// failures roll the <c>RetryCount</c>/<c>LastError</c>/<c>LastActivity</c>/<c>NextRetryAt</c>
/// up with the activity's exponential-backoff policy.
/// </summary>
public sealed class StoreService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly AppContext _ctx;

    public StoreService(AppContext ctx) => _ctx = ctx;

    public async Task<SaveBatchResult> ProcessAsync(IReadOnlyList<MasterRecord> records, CancellationToken ct = default)
    {
        var success = 0;
        var failure = 0;

        for (var idx = 0; idx < records.Count; idx++)
        {
            var record = records[idx];
            var individual = await FetchAndAttachAsync(record, ct);

            if (individual is null)
            {
                await FailAsync(record, "CRM returned no data for customer", ActivityTypeCodes.Crm, ct);
                failure++;
                continue;
            }

            if (ShouldSimulateSaveError(record, idx))
            {
                // Simulated persistence failure to exercise the retry path.
                await FailAsync(record, "Simulated database save error (training scenario)", ActivityTypeCodes.Store, ct);
                failure++;
                continue;
            }

            try
            {
                var save = await _ctx.Individuals.SaveAsync(individual, ct);
                if (!save.Success)
                {
                    await FailAsync(record, save.Error ?? "Save returned failure", ActivityTypeCodes.Store, ct);
                    failure++;
                    continue;
                }

                await _ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.Saved, save.Summary, null, ct);
                await LogAttemptAsync(record, ActivityTypeCodes.Store, MasterRecordStatus.Saved, true, null, save.Summary, ct);
                success++;
                Log.Info("[store] [{CustomerId}] saved: {Summary}", record.CustomerId, save.Summary);
            }
            catch (Exception ex)
            {
                await FailAsync(record, ex.Message, ActivityTypeCodes.Store, ct);
                failure++;
            }
        }

        return new SaveBatchResult(success, failure, records.Count);
    }

    private async Task<Core.Domain.Individual?> FetchAndAttachAsync(MasterRecord record, CancellationToken ct)
    {
        var individual = await _ctx.Crm.GetCustomerAsync(record.CustomerId, ct);
        if (individual is null)
        {
            await LogAttemptAsync(record, ActivityTypeCodes.Crm, MasterRecordStatus.Failed, false,
                "CRM returned no data for customer", "CRM returned no data", ct);
            return null;
        }

        await _ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.CrmFetched, "CRM data fetched", null, ct);
        await LogAttemptAsync(record, ActivityTypeCodes.Crm, MasterRecordStatus.CrmFetched, true, null, "CRM data fetched", ct);

        individual.Id = 0;
        individual.MasterRecordId = record.Id;
        individual.CustomerId = record.CustomerId;
        return individual;
    }

    private bool ShouldSimulateSaveError(MasterRecord record, int index)
    {
        var sim = _ctx.Settings.Simulation;
        if (!sim.SaveErrorsEnabled) return false;
        if (!string.IsNullOrEmpty(sim.SaveErrorForCustomerId) && record.CustomerId == sim.SaveErrorForCustomerId) return true;
        if (sim.SaveErrorEvery > 0 && (index + 1) % sim.SaveErrorEvery == 0) return true;
        return false;
    }

    /// <summary>
    /// Marks the record Failed and rolls the retry bookkeeping up with the activity's
    /// exponential-backoff policy. Once the activity's budget is exhausted the record is
    /// flagged for reconciliation (manual intervention).
    /// </summary>
    private async Task FailAsync(MasterRecord record, string error, string activityCode, CancellationToken ct)
    {
        var activity = await _ctx.Master.GetActivityTypeByCodeAsync(activityCode, ct);
        var attempt = record.RetryCount + 1;
        var retryable = activity is { IsRetryable: true };
        var nextRetryAt = retryable
            ? DateTime.UtcNow.AddHours(activity!.BackoffHoursAfter(attempt))
            : (DateTime?)null;
        var exhausted = retryable && activity!.IsExhausted(attempt);

        await _ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.Failed, null, error, ct);
        await _ctx.Master.RecordRetryAsync(record.Id, attempt, error, activityCode, nextRetryAt, exhausted, ct);
        await LogAttemptAsync(record, activityCode, MasterRecordStatus.Failed, false, error,
            $"retry {attempt}{(exhausted ? " (budget exhausted -> reconcile)" : "")}", ct,
            activity?.Id, nextRetryAt);

        if (exhausted)
            Log.Warn("[store] [{CustomerId}] FAILED (retry {Attempt}) [{ActivityCode}]: {Error} -> flagged for reconciliation",
                record.CustomerId, attempt, activityCode, error);
        else
            Log.Warn("[store] [{CustomerId}] FAILED (retry {Attempt}) [{ActivityCode}]: {Error} -> next retry {NextRetryAt:u}",
                record.CustomerId, attempt, activityCode, error, nextRetryAt);
    }

    private Task<int> LogAttemptAsync(MasterRecord record, string stage, MasterRecordStatus status, bool success,
        string? error, string? remarks, CancellationToken ct, long? activityTypeId = null, DateTime? nextRetryAt = null)
        => _ctx.Master.LogAttemptAsync(new MasterRecordAttempt
        {
            MasterRecordId = record.Id,
            CustomerId = record.CustomerId,
            Stage = stage,
            ActivityTypeId = activityTypeId,
            Status = (int)status,
            Success = success,
            Error = error,
            Remarks = remarks,
            AttemptedAt = DateTime.UtcNow,
            NextRetryAt = nextRetryAt,
        }, ct);
}
