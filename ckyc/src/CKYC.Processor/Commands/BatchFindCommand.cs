using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Shows every upload batch that has contained an organization customer.</summary>
public sealed class BatchFindCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "batch-find";
    public string Usage => "CKYCProcessor.exe batch-find --customer <customerId>";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var customerId = Option(args, "--customer");
        if (string.IsNullOrWhiteSpace(customerId))
        {
            Log.Error("[batch-find] Pass --customer <customerId>.");
            return 1;
        }

        var rows = await ctx.Master.GetBatchHistoryAsync(customerId, ct);
        if (rows.Count == 0)
        {
            Log.Warn("[batch-find] No batch membership found for '{CustomerId}'.", customerId);
            return 1;
        }

        Log.Info("[batch-find] {CustomerId} belongs to {BatchCount} batch(es):", customerId, rows.Count);
        foreach (var row in rows)
            Log.Info("[batch-find]   {BatchFile}  record20-line={Record20Line}  batched={BatchedAt:O}",
                row.BatchFile, row.Record20LineNumber?.ToString() ?? "?", row.BatchedAt);
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
