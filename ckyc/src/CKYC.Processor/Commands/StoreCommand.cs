using CKYC.Core.Domain;

namespace CKYC.Processor.Commands;

/// <summary>Step 3 — enrich Pending master records from the CRM and save individual details.</summary>
public sealed class StoreCommand : ICommand
{
    public string Name => "store";
    public string Usage => "CKYCProcessor.exe store [--limit N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var pending = await ctx.Master.GetByStatusAsync(MasterRecordStatus.Pending, limit, ct);
        if (pending.Count == 0)
        {
            Console.WriteLine("[store] No Pending records to process.");
            return 0;
        }

        Console.WriteLine($"[store] Processing {pending.Count} pending master record(s) through the CRM...");
        var service = new StoreService(ctx);
        var result = await service.ProcessAsync(pending, ct);
        Console.WriteLine($"[store] Done: Succeeded={result.Succeeded}  Failed={result.Failed}  Total={result.Total}");
        return result.Failed > 0 ? 1 : 0;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }
}
