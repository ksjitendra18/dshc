using System.Text;
using CKYC.Core.Domain;

namespace CKYC.Processor.Commands;

/// <summary>
/// Reconciliation report. Serves a report of records that need <b>manual intervention</b> to
/// the respective stakeholder. Two sources feed it:
/// <list type="bullet">
///   <item>records that have <b>exhausted their retry attempt</b> (still failed, no more
///        automatic retries) — <c>--kind retry</c>;</item>
///   <item>records that <b>failed at CERSAI</b> (rejected, or FVU-upload failed) — <c>--kind cersai</c>.</item>
/// </list>
/// The report is written to a CSV (one row per record, with the status, retry/attempt history
/// and the latest CERSAI reply) for the stakeholder to review and act on.
///
/// Usage:
///   CKYCProcessor.exe reconcile                    # all records needing intervention
///   CKYCProcessor.exe reconcile --kind retry       # retry-exhausted only
///   CKYCProcessor.exe reconcile --kind cersai      # CERSAI-failed only
///   CKYCProcessor.exe reconcile --out <path> --stakeholder "Operations"
/// </summary>
public sealed class ReconcileCommand : ICommand
{
    public string Name => "reconcile";
    public string Usage => "CKYCProcessor.exe reconcile [--kind retry|cersai] [--out <path>] [--stakeholder <name>]";

    private static readonly string[] Header =
    {
        "MasterRecordId", "CustomerId", "BusinessDate", "Status", "FailedStage",
        "RetryCount", "LastError", "LastAttemptAt", "NextRetryAt", "NeedsReconcile",
        "ReconStatus", "ReconRemarks", "CersaiStatus", "CersaiAckNumber",
        "CersaiRejectionRemark", "CersaiResponseReadAt", "ReattemptCount",
    };

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var kind = Option(args, "--kind");
        if (kind is not null && kind is not ("retry" or "cersai"))
        {
            Console.Error.WriteLine("[reconcile] --kind must be 'retry' or 'cersai'.");
            return 1;
        }
        var stakeholder = Option(args, "--stakeholder") ?? "Stakeholder";
        var limit = OptionInt(args, "--limit") ?? int.MaxValue;

        var records = await ctx.Master.GetNeedsReconcileAsync(kind, limit, ct);
        if (records.Count == 0)
        {
            Console.WriteLine($"[reconcile] No records need manual intervention (kind={kind ?? "all"}).");
            return 0;
        }

        var path = Option(args, "--out") ?? DefaultPath(ctx);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var lines = new StringBuilder();
        lines.AppendLine($"# Reconciliation report for {stakeholder}");
        lines.AppendLine($"# Generated {DateTime.UtcNow:u}  kind={kind ?? "all"}  records={records.Count}");
        lines.AppendLine(string.Join(",", Header.Select(EscapeCsv)));

        foreach (var r in records)
        {
            lines.AppendLine(string.Join(",", new[]
            {
                r.Id.ToString(), r.CustomerId, r.BusinessDate.ToString("yyyy-MM-dd"),
                r.Status.Label(), r.LastActivity ?? "", r.RetryCount.ToString(),
                r.LastError ?? "", r.LastAttemptAt?.ToString("o") ?? "", r.NextRetryAt?.ToString("o") ?? "",
                r.NeedsReconcile ? "Yes" : "No", r.ReconStatus ?? "", r.ReconRemarks ?? "",
                r.LastResponseStatus ?? "", r.LastResponseAckNumber ?? "",
                r.LastResponseRejectionRemark ?? "", r.LastResponseReadAt?.ToString("o") ?? "",
                r.ReattemptCount.ToString(),
            }.Select(EscapeCsv)));
        }

        await File.WriteAllTextAsync(path, lines.ToString(), new UTF8Encoding(false), ct);

        Console.WriteLine($"[reconcile] {records.Count} record(s) need manual intervention -> {Path.GetFullPath(path)}");
        Console.WriteLine($"[reconcile] Stakeholder: {stakeholder}");
        foreach (var r in records)
        {
            var reason = r.NeedsReconcile
                ? $"exhausted retries (last error: {r.LastError})"
                : (r.Status == MasterRecordStatus.Rejected ? "rejected by CERSAI" : "failed at CERSAI");
            Console.WriteLine($"  [{r.Id}] {r.CustomerId}  {r.Status.Label()}  retry={r.RetryCount}  reattempt={r.ReattemptCount}  {reason}");
            if (r.LastResponseRejectionRemark is not null)
                Console.WriteLine($"        CERSAI remark: {r.LastResponseRejectionRemark}");
        }
        return 0;
    }

    private static string DefaultPath(AppContext ctx)
    {
        var root = string.IsNullOrWhiteSpace(ctx.Settings.Batch.OutputRoot)
            ? Directory.GetCurrentDirectory()
            : ctx.Settings.Batch.OutputRoot;
        return Path.Combine(root, $"reconciliation_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }
}
