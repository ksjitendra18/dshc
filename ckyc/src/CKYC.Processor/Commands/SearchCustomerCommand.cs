using CKYC.Core.Domain;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Pre-batch customer search (individual). Processes every master record in the
/// <see cref="MasterRecordStatus.PendingSearch"/> state: builds the per-customer search rows
/// in <c>individual_search</c>, calls the search API for each, and resolves the record to
/// <see cref="MasterRecordStatus.SearchFound"/> (an existing CKYC record — journey ends) or
/// <see cref="MasterRecordStatus.Searched"/> (search key written to record 20, ready to batch).
///
/// Usage:
///   CKYCProcessor.exe search-customer [--limit N]
///   CKYCProcessor.exe search-customer --customer CUST202608240001
/// </summary>
public sealed class SearchCustomerCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "search-customer";
    public string Usage => "CKYCProcessor.exe search-customer [--limit N] [--customer <customerId>]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var customer = Option(args, "--customer");

        IReadOnlyList<MasterRecord> records;
        if (customer is not null)
        {
            var matches = await ctx.Master.GetByCustomerIdsAsync(new[] { customer }, ct);
            records = matches.Where(r => string.Equals(r.ClientType, "I", StringComparison.OrdinalIgnoreCase)
                                      && r.Status is not (MasterRecordStatus.ImagePending or MasterRecordStatus.ImageFailed)).ToList();
            if (records.Count == 0)
            {
                Log.Warn("[search-customer] No individual master record found for customer '{CustomerId}' (or it is still awaiting its image/document fetch).", customer);
                return 1;
            }
        }
        else
        {
            records = await ctx.Master.GetByStatusAsync(MasterRecordStatus.PendingSearch, limit, "I", ct);
            if (records.Count == 0)
            {
                Log.Info("[search-customer] No records pending search. Run `store` first.");
                return 0;
            }
        }

        Log.Info("[search-customer] Processing {Count} individual record(s) through the customer-search API...", records.Count);
        var service = new IndividualSearchService(ctx);
        int found = 0, notFound = 0, failed = 0;

        foreach (var record in records)
        {
            var outcome = await service.ProcessAsync(record, ct);
            switch (outcome)
            {
                case MasterRecordStatus.SearchFound: found++; break;
                case MasterRecordStatus.Searched: notFound++; break;
                default: failed++; break;
            }
        }

        Log.Info("[search-customer] Done: Found={Found}  NotFound={NotFound}  Failed={Failed}  Total={Total}",
            found, notFound, failed, records.Count);
        if (notFound > 0)
            Log.Info("[search-customer] {NotFound} record(s) are now Searched (search key in record 20) -> run `build-zip` next.", notFound);
        if (found > 0)
            Log.Info("[search-customer] {Found} record(s) already exist in CKYC (SearchFound) and are excluded from batching.", found);
        return failed > 0 ? 1 : 0;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
