using CKYC.Core.Domain;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Retries failed master records that are within their retry budget and whose exponential
/// backoff has elapsed, per the retryable activity the record last failed on.
///
/// Usage:
///   CKYCProcessor.exe retry                     # retry all retryable activities due now
///   CKYCProcessor.exe retry --activity CbsFetch  # retry only the CBS fetch
///   CKYCProcessor.exe retry --limit N
/// </summary>
public sealed class RetryCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "retry";
    public string Usage => "CKYCProcessor.exe retry [--activity <code>] [--limit N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var activityCode = Option(args, "--activity");

        var result = await RetryService.RunAsync(ctx, activityCode, limit, ct);

        Log.Info("[retry] Done: Attempted={Attempted}  Succeeded={Succeeded}  PermanentFailed={PermanentFailed}  Skipped/DueLater={Skipped}",
            result.Attempted, result.Succeeded, result.PermanentFailed, result.Skipped);
        if (result.PermanentFailed > 0)
            Log.Warn("[retry] Some records exhausted their retry budget -> run `reconcile` for the manual-intervention report.");
        return result.PermanentFailed > 0 || result.Skipped > 0 ? 1 : 0;
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
