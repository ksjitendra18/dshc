using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Processor.Commands;

/// <summary>
/// Step 4 (client type L) — validate, generate the pipe-delimited .UPL file from saved
/// legal-entity records and zip it. Mirrors <see cref="BuildZipCommand"/> for the
/// individual/retail client type, but operates on the dedicated legal-entity record tables
/// and marks the matching master rows with client type "L".
/// </summary>
public sealed class BuildZipLegalCommand : ICommand
{
    public string Name => "build-zip-legal";
    public string Usage => "CKYCProcessor.exe build-zip-legal [--limit N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var saved = await ctx.Master.GetByStatusAsync(MasterRecordStatus.Saved, limit, "L", ct);
        if (saved.Count == 0)
        {
            Console.WriteLine("[build-zip-legal] No Saved legal-entity records to batch. Run `insert-legal` first.");
            return 0;
        }

        var customerIds = saved.Select(r => r.SourceCustomerId).ToList();
        var entities = await ctx.LegalEntities.GetBySourceCustomerIdsAsync(customerIds, ct);
        var ordered = customerIds
            .Select(id => entities.FirstOrDefault(e => e.SourceCustomerId == id))
            .Where(e => e is not null)
            .Cast<Core.Domain.LegalEntity>()
            .ToList();

        if (ordered.Count == 0)
        {
            Console.WriteLine("[build-zip-legal] No stored legal-entity records matched the saved master records.");
            return 0;
        }

        var batch = await ctx.LegalEntityBatchGenerator.GenerateAsync(ordered, DateOnly.FromDateTime(DateTime.Today), ct);

        var skippedIds = batch.Skipped is null ? new HashSet<string>() : new HashSet<string>(batch.Skipped.Select(s => s.SourceCustomerId));
        var batchedRecords = saved.Where(r => !skippedIds.Contains(r.SourceCustomerId)).ToList();
        var batchedIds = batchedRecords.Select(r => r.Id).ToList();

        var lineByRecord = new Dictionary<long, int>();
        if (batch.Record20Lines is not null)
            foreach (var r in batchedRecords)
                if (batch.Record20Lines.TryGetValue(r.SourceCustomerId, out var line))
                    lineByRecord[r.Id] = line;

        await ctx.Master.MarkBatchAsync(batchedIds, batch.UploadFileName, lineByRecord, ct);
        await ctx.Journal.LogBatchAsync(batch, ct);

        var buildActivity = await ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.BuildZip, ct);
        foreach (var r in batchedRecords)
            await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
            {
                MasterRecordId = r.Id,
                SourceCustomerId = r.SourceCustomerId,
                Stage = "BuildZip",
                ActivityTypeId = buildActivity?.Id,
                Status = (int)MasterRecordStatus.Batched,
                Success = true,
                Remarks = $"Batched legal entity into '{batch.UploadFileName}' at record-20 line {lineByRecord.GetValueOrDefault(r.Id)}",
            }, ct);
        foreach (var s in batch.Skipped ?? Array.Empty<SkippedRecord>())
            foreach (var r in saved.Where(x => x.SourceCustomerId == s.SourceCustomerId))
                await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
                {
                    MasterRecordId = r.Id,
                    SourceCustomerId = r.SourceCustomerId,
                    Stage = "BuildZip",
                    ActivityTypeId = buildActivity?.Id,
                    Status = (int)MasterRecordStatus.Saved,
                    Success = false,
                    Remarks = string.Join("; ", s.Errors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}")),
                }, ct);

        Console.WriteLine($"[build-zip-legal] Batch '{batch.BatchKey}' generated with {batch.RecordCount} legal entity record(s).");
        Console.WriteLine($"[build-zip-legal]   Upload file : {batch.UploadFilePath}");
        Console.WriteLine($"[build-zip-legal]   Zip archive : {batch.ZipPath}");

        if (batch.SkippedCount > 0)
        {
            Console.WriteLine($"[build-zip-legal]   Skipped     : {batch.SkippedCount} record(s) failed validation:");
            foreach (var s in batch.Skipped!)
            {
                Console.WriteLine($"    ! {s.SourceCustomerId} ({s.CustomerName})");
                foreach (var e in s.Errors)
                    Console.WriteLine($"        - [{e.RecordType}/{e.FieldName}] {e.ErrorDescription}");
            }
        }
        else
        {
            Console.WriteLine("[build-zip-legal]   Skipped     : none");
        }

        Console.WriteLine("[build-zip-legal] Run `fvu` to submit this batch to the File Validation Utility.");
        return 0;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }
}
