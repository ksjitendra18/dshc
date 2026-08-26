using CKYC.Core.Domain;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Show a pipeline status snapshot (master-table counts by current stage + last batch/FVU run).</summary>
public sealed class StatusCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "status";
    public string Usage => "CKYCProcessor.exe status";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        Log.Info("=== CKYC master-table status (current stage per record) ===");
        foreach (var status in Enum.GetValues<MasterRecordStatus>())
        {
            var count = await ctx.Master.CountByStatusAsync(status, ct);
            if (count > 0 || status <= MasterRecordStatus.FvuPassed)
                Log.Info("  {Status,-28} : {Count}", status.Label(), count);
        }

        var last = await ctx.Journal.GetLastBatchAsync(ct);
        Log.Info("=== Last batch ===");
        if (last is not null)
        {
            Log.Info("  key={BatchKey}  file={UploadFileName}  records={RecordCount}", last.BatchKey, last.UploadFileName, last.RecordCount);
            Log.Info("  upload={UploadFilePath}", last.UploadFilePath);
            Log.Info("  zip={ZipPath}", last.ZipPath);
            Log.Info("  Next: `fvu` to submit, then `response read` to ingest the CERSAI reply.");
        }
        else
        {
            Log.Info("  (none yet — run `build-zip`)");
        }
        return 0;
    }
}
