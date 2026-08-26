using CKYC.Core.Domain;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Step 3 — enrich Pending master records from the CRM and save individual details.</summary>
public sealed class StoreCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "store";
    public string Usage => "CKYCProcessor.exe store [--limit N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var pending = await ctx.Master.GetByStatusAsync(MasterRecordStatus.Pending, limit, ct: ct);
        if (pending.Count == 0)
        {
            Log.Info("[store] No Pending records to process.");
            return 0;
        }

        Log.Info("[store] Processing {Count} pending master record(s) through the CRM...", pending.Count);
        var service = new StoreService(ctx);
        var result = await service.ProcessAsync(pending, ct);
        Log.Info("[store] Done: Succeeded={Succeeded}  Failed={Failed}  Total={Total}", result.Succeeded, result.Failed, result.Total);
        return result.Failed > 0 ? 1 : 0;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }
}
