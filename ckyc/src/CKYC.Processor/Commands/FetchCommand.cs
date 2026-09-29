using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Step 1 — fetch the daily customer ids into the master table. This is the
/// <b>CBS</b> (Core Banking System) fetch, the retryable example: with the CBS simulation
/// enabled, a deterministic subset of customer ids fails to be fetched and is recorded as a
/// retryable <c>Failed</c> master row (with the activity's exponential-backoff schedule),
/// so the <c>retry</c> command can re-attempt them.
/// </summary>
public sealed class FetchCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "fetch";
    public string Usage => "CKYCProcessor.exe fetch cust [--file custid.json] [--date yyyy-MM-dd] [--count N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var date = OptionDate(args, "--date") ?? DateOnly.FromDateTime(DateTime.Today);
        var file = Option(args, "--file");

        IReadOnlyList<SourceCustomer> customers;
        if (file is not null)
        {
            customers = DailyCustomerIdProvider.ReadCustomersFile(file, ctx.Settings.Source.DocumentKeyProperty);
            Log.Info("[fetch] Reading {Count} customer source record(s) from '{Path}'", customers.Count, Path.GetFullPath(file));
        }
        else if (args.Any(a => string.Equals(a, "custid", StringComparison.OrdinalIgnoreCase)))
        {
            var custFile = ResolveCustIdFile();
            if (custFile is null)
            {
                Log.Error("[fetch] 'custid' requested but no custid.json was found (looked in the current directory and the app directory).");
                return 1;
            }
            customers = DailyCustomerIdProvider.ReadCustomersFile(custFile, ctx.Settings.Source.DocumentKeyProperty);
            Log.Info("[fetch] Reading {Count} customer source record(s) from '{Path}'", customers.Count, custFile);
        }
        else
        {
            customers = ctx.CustomerIds.GetCustomers(date);
            Log.Info("[fetch] Source customer ids for {Date}: {Count}", date, customers.Count);
        }

        if (customers.Count == 0)
        {
            Log.Warn("[fetch] No customer ids found.");
            return 1;
        }

        // Split the source set into records that fetched cleanly and ids where the CBS call
        // failed (only when the CBS simulation is enabled — off by default).
        var (ok, failed) = Partition(ctx, customers);
        if (failed.Count > 0)
            foreach (var customer in failed)
                await CbsFailAsync(ctx, customer.CustomerId, date, ct);

        var result = await ctx.Master.UpsertCustomersAsync(ok, date, ct);

        Log.Info("[fetch] Inserted={Inserted}  Skipped={Skipped}  Total={Total}  CbsFailed={CbsFailed}", result.Inserted, result.Skipped, result.Total, failed.Count);
        Log.Info("[fetch] Master table rows now in Pending state -> run `store` to enrich from the CRM.");

        return failed.Count > 0 ? 1 : 0;
    }

    private static (IReadOnlyList<SourceCustomer> Ok, IReadOnlyList<SourceCustomer> Failed) Partition(AppContext ctx, IReadOnlyList<SourceCustomer> customers)
    {
        var sim = ctx.Settings.Simulation;
        if (!sim.CbsFetchErrorsEnabled || sim.CbsFetchFailEvery <= 0) return (customers, Array.Empty<SourceCustomer>());

        var ok = new List<SourceCustomer>();
        var failed = new List<SourceCustomer>();
        for (var i = 0; i < customers.Count; i++)
        {
            var customer = customers[i];
            var fail = (!string.IsNullOrEmpty(sim.CbsFetchFailForCustomerId) && customer.CustomerId == sim.CbsFetchFailForCustomerId)
                       || ((i + 1) % sim.CbsFetchFailEvery == 0);
            (fail ? failed : ok).Add(customer);
        }
        return (ok, failed);
    }

    /// <summary>
    /// Records a CBS fetch failure for a customer id: get-or-create the master row, mark it
    /// <c>Failed</c>, log the retryable attempt with the exponential-backoff next-retry time
    /// and flag it for reconciliation once the budget is exhausted.
    /// </summary>
    private static async Task CbsFailAsync(AppContext ctx, string customerId, DateOnly businessDate, CancellationToken ct)
    {
        var record = await ctx.Master.EnsureAsync(customerId, businessDate, ct: ct);
        var activity = await ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.CbsFetch, ct);
        var attempt = record.RetryCount + 1;
        var error = $"CBS fetch failed for customer '{customerId}'";
        var retryable = activity is { IsRetryable: true };
        var nextRetryAt = retryable
            ? DateTime.UtcNow.AddHours(activity!.BackoffHoursAfter(attempt))
            : (DateTime?)null;
        var exhausted = retryable && activity!.IsExhausted(attempt);

        await ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.Failed, null, error, ct);
        await ctx.Master.RecordRetryAsync(record.Id, attempt, error, ActivityTypeCodes.CbsFetch, nextRetryAt, exhausted, ct);
        await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
        {
            MasterRecordId = record.Id,
            CustomerId = customerId,
            Stage = ActivityTypeCodes.CbsFetch,
            ActivityTypeId = activity?.Id,
            Status = (int)MasterRecordStatus.Failed,
            Success = false,
            Error = error,
            AttemptedAt = DateTime.UtcNow,
            NextRetryAt = nextRetryAt,
        }, ct);

        if (exhausted)
            Log.Warn("[fetch] [{CustomerId}] CBS fetch FAILED (retry {Attempt}) -> flagged for reconciliation", customerId, attempt);
        else
            Log.Warn("[fetch] [{CustomerId}] CBS fetch FAILED (retry {Attempt}) -> next retry {NextRetryAt:u}", customerId, attempt, nextRetryAt);
    }

    private static string? ResolveCustIdFile()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "custid.json"),
            Path.Combine(System.AppContext.BaseDirectory, "custid.json"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static DateOnly? OptionDate(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        if (i >= 0 && i + 1 < args.Length && DateOnly.TryParse(args[i + 1], out var d)) return d;
        return null;
    }
}
