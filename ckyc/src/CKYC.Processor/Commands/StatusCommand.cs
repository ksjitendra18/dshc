using CKYC.Core.Domain;

namespace CKYC.Processor.Commands;

/// <summary>Show a pipeline status snapshot (master-table counts by current stage + last batch/FVU run).</summary>
public sealed class StatusCommand : ICommand
{
    public string Name => "status";
    public string Usage => "CKYCProcessor.exe status";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        Console.WriteLine("=== CKYC master-table status (current stage per record) ===");
        foreach (var status in Enum.GetValues<MasterRecordStatus>())
        {
            var count = await ctx.Master.CountByStatusAsync(status, ct);
            if (count > 0 || status <= MasterRecordStatus.FvuPassed)
                Console.WriteLine($"  {status.Label(),-28} : {count}");
        }

        var last = await ctx.Journal.GetLastBatchAsync(ct);
        Console.WriteLine("=== Last batch ===");
        if (last is not null)
        {
            Console.WriteLine($"  key={last.BatchKey}  file={last.UploadFileName}  records={last.RecordCount}");
            Console.WriteLine($"  upload={last.UploadFilePath}");
            Console.WriteLine($"  zip={last.ZipPath}");
            Console.WriteLine("  Next: `fvu` to submit, then `response read` to ingest the CERSAI reply.");
        }
        else
        {
            Console.WriteLine("  (none yet — run `build-zip`)");
        }
        return 0;
    }
}
