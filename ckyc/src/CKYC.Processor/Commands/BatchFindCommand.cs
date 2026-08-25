namespace CKYC.Processor.Commands;

/// <summary>Shows every upload batch that has contained an organization customer.</summary>
public sealed class BatchFindCommand : ICommand
{
    public string Name => "batch-find";
    public string Usage => "CKYCProcessor.exe batch-find --customer <customerId>";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var customerId = Option(args, "--customer");
        if (string.IsNullOrWhiteSpace(customerId))
        {
            Console.Error.WriteLine("[batch-find] Pass --customer <customerId>.");
            return 1;
        }

        var rows = await ctx.Master.GetBatchHistoryAsync(customerId, ct);
        if (rows.Count == 0)
        {
            Console.WriteLine($"[batch-find] No batch membership found for '{customerId}'.");
            return 1;
        }

        Console.WriteLine($"[batch-find] {customerId} belongs to {rows.Count} batch(es):");
        foreach (var row in rows)
            Console.WriteLine($"  {row.BatchFile}  record20-line={row.Record20LineNumber?.ToString() ?? "?"}  batched={row.BatchedAt:O}");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
