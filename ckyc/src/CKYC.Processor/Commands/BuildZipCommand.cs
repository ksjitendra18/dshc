using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Processor.Commands;

/// <summary>Step 4 — validate, generate the pipe-delimited .UPL file from saved records and zip it.</summary>
public sealed class BuildZipCommand : ICommand
{
    public string Name => "build-zip";
    public string Usage => "CKYCProcessor.exe build-zip [--limit N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var saved = await ctx.Master.GetByStatusAsync(MasterRecordStatus.Saved, limit, ct);
        if (saved.Count == 0)
        {
            Console.WriteLine("[build-zip] No Saved records to batch. Run `store` first.");
            return 0;
        }

        var customerIds = saved.Select(r => r.SourceCustomerId).ToList();
        var individuals = await ctx.Individuals.GetBySourceCustomerIdsAsync(customerIds, ct);
        var orderedIndividuals = customerIds
            .Select(id => individuals.FirstOrDefault(i => i.SourceCustomerId == id))
            .Where(i => i is not null)
            .Cast<Core.Domain.Individual>()
            .ToList();

        if (orderedIndividuals.Count == 0)
        {
            Console.WriteLine("[build-zip] No stored individual records matched the saved master records.");
            return 0;
        }

        var batch = await ctx.BatchGenerator.GenerateAsync(orderedIndividuals, DateOnly.FromDateTime(DateTime.Today), ct);

        // Only the records that actually made it into the batch are marked as batched —
        // records excluded by validation stay in the Saved state so they can be fixed/retried.
        var skippedIds = batch.Skipped is null ? new HashSet<string>() : new HashSet<string>(batch.Skipped.Select(s => s.SourceCustomerId));
        var batchedRecords = saved.Where(r => !skippedIds.Contains(r.SourceCustomerId)).ToList();
        var batchedIds = batchedRecords.Select(r => r.Id).ToList();

        // Capture each record's record-20 line number in the batch so a CERSAI response
        // can be attributed back to the correct master record later.
        var lineByRecord = new Dictionary<long, int>();
        if (batch.Record20Lines is not null)
            foreach (var r in batchedRecords)
                if (batch.Record20Lines.TryGetValue(r.SourceCustomerId, out var line))
                    lineByRecord[r.Id] = line;

        await ctx.Master.MarkBatchAsync(batchedIds, batch.UploadFileName, lineByRecord, ct);
        await ctx.Journal.LogBatchAsync(batch, ct);

        // Audit trail: log a BuildZip attempt for every record that was processed.
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
                Remarks = $"Batched into '{batch.UploadFileName}' at record-20 line {lineByRecord.GetValueOrDefault(r.Id)}",
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

        Console.WriteLine($"[build-zip] Batch '{batch.BatchKey}' generated with {batch.RecordCount} record(s).");
        Console.WriteLine($"[build-zip]   Upload file : {batch.UploadFilePath}");
        Console.WriteLine($"[build-zip]   Zip archive : {batch.ZipPath}");

        if (batch.SkippedCount > 0)
        {
            Console.WriteLine($"[build-zip]   Skipped     : {batch.SkippedCount} record(s) failed validation and were excluded:");
            foreach (var s in batch.Skipped!)
            {
                Console.WriteLine($"    ! {s.SourceCustomerId} ({s.CustomerName})");
                foreach (var e in s.Errors)
                    Console.WriteLine($"        - [{e.RecordType}/{e.FieldName}] {e.ErrorDescription}");
            }
        }
        else
        {
            Console.WriteLine("[build-zip]   Skipped     : none");
        }

        Console.WriteLine("[build-zip] Run `fvu` to submit this batch to the File Validation Utility.");
        return 0;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }
}
