using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Step 2 — run the bundled dummy CRM API (blocks until stopped).</summary>
public sealed class CrmServeCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "crm";
    public string Usage => "CKYCProcessor.exe crm serve [--urls http://127.0.0.1:5291]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var urls = Option(args, "--urls") ?? ctx.Settings.Crm.BaseUrl;
        Log.Info("[crm] Starting dummy CRM API on {Urls}", urls);
        Log.Info("[crm]   GET /api/customers       -> daily customer ids");
        Log.Info("[crm]   GET /api/customers/{{id}}  -> individual KYC record");
        Log.Info("[crm]   GET /health              -> liveness");
        Log.Info("[crm] Press Ctrl+C to stop.");
        await ctx.CrmServer.RunAsync(urls, ct);
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
