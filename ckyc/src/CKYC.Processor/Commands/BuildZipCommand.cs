using CKYC.Core.Domain;
using CKYC.Core.Models;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Step 4 — validate, generate the pipe-delimited .UPL file from saved records and zip it.</summary>
public sealed class BuildZipCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "build-zip";
    public string Usage => "CKYCProcessor.exe build-zip [--limit N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var requestedLimit = OptionInt(args, "--limit") ?? CKYC.Core.Spec.CkycRecords.MaxIndividualBatchRecords;
        var limit = Math.Clamp(requestedLimit, 1, CKYC.Core.Spec.CkycRecords.MaxIndividualBatchRecords);
        var saved = await ctx.Master.GetByStatusAsync(MasterRecordStatus.Saved, limit, "I", ct);
        if (saved.Count == 0)
        {
            Log.Info("[build-zip] No Saved records to batch. Run `store` first.");
            return 0;
        }

        var customerIds = saved.Select(r => r.CustomerId).ToList();
        var individuals = await ctx.Individuals.GetByCustomerIdsAsync(customerIds, ct);
        // O(n) lookup instead of an O(n²) FirstOrDefault scan per customer id.
        var byCustomerId = individuals.ToDictionary(i => i.CustomerId, StringComparer.Ordinal);
        var orderedIndividuals = customerIds
            .Where(byCustomerId.ContainsKey)
            .Select(id => byCustomerId[id])
            .ToList();

        if (orderedIndividuals.Count == 0)
        {
            Log.Info("[build-zip] No stored individual records matched the saved master records.");
            return 0;
        }

        var batch = await ctx.BatchGenerator.GenerateAsync(orderedIndividuals, DateOnly.FromDateTime(DateTime.Today), ct);

        // Only the records that actually made it into the batch are marked as batched —
        // records excluded by validation stay in the Saved state so they can be fixed/retried.
        var skippedIds = batch.Skipped is null ? new HashSet<string>() : new HashSet<string>(batch.Skipped.Select(s => s.CustomerId));
        var batchedRecords = saved.Where(r => !skippedIds.Contains(r.CustomerId)).ToList();
        var batchedIds = batchedRecords.Select(r => r.Id).ToList();

        // Capture each record's record-20 line number in the batch so a CERSAI response
        // can be attributed back to the correct master record later.
        var lineByRecord = new Dictionary<long, int>();
        if (batch.Record20Lines is not null)
            foreach (var r in batchedRecords)
                if (batch.Record20Lines.TryGetValue(r.CustomerId, out var line))
                    lineByRecord[r.Id] = line;

        await ctx.Master.MarkBatchAsync(batchedIds, batch.UploadFileName, lineByRecord, ct);
        await ctx.Journal.LogBatchAsync(batch, ct);

        // Audit trail: log a BuildZip attempt for every record that was processed.
        var buildActivity = await ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.BuildZip, ct);
        foreach (var r in batchedRecords)
            await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
            {
                MasterRecordId = r.Id,
                CustomerId = r.CustomerId,
                Stage = "BuildZip",
                ActivityTypeId = buildActivity?.Id,
                Status = (int)MasterRecordStatus.Batched,
                Success = true,
                Remarks = $"Batched into '{batch.UploadFileName}' at record-20 line {lineByRecord.GetValueOrDefault(r.Id)}",
            }, ct);
        foreach (var s in batch.Skipped ?? Array.Empty<SkippedRecord>())
            foreach (var r in saved.Where(x => x.CustomerId == s.CustomerId))
                await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
                {
                    MasterRecordId = r.Id,
                    CustomerId = r.CustomerId,
                    Stage = "BuildZip",
                    ActivityTypeId = buildActivity?.Id,
                    Status = (int)MasterRecordStatus.Saved,
                    Success = false,
                    Remarks = string.Join("; ", s.Errors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}")),
                }, ct);

        Log.Info("[build-zip] Batch '{BatchKey}' generated with {RecordCount} record(s).", batch.BatchKey, batch.RecordCount);
        Log.Info("[build-zip]   Upload file : {UploadFilePath}", batch.UploadFilePath);
        Log.Info("[build-zip]   Zip archive : {ZipPath}", batch.ZipPath);

        if (batch.SkippedCount > 0)
        {
            Log.Warn("[build-zip]   Skipped     : {SkippedCount} record(s) failed validation and were excluded:", batch.SkippedCount);
            foreach (var s in batch.Skipped!)
            {
                Log.Warn("[build-zip]     ! {CustomerId} ({CustomerName})", s.CustomerId, s.CustomerName);
                foreach (var e in s.Errors)
                    Log.Warn("[build-zip]         - [{RecordType}/{FieldName}] {ErrorDescription}", e.RecordType, e.FieldName, e.ErrorDescription);
            }
        }
        else
        {
            Log.Info("[build-zip]   Skipped     : none");
        }

        Log.Info("[build-zip] Run `fvu` to submit this batch to the File Validation Utility.");
        // A partial batch is produced safely, but a non-zero exit lets automation detect
        // that one or more requested records failed pre-flight validation.
        return batch.SkippedCount > 0 ? 1 : 0;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }
}
