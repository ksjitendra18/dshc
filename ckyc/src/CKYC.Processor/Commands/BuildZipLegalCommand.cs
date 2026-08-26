using CKYC.Core.Domain;
using CKYC.Core.Models;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Step 4 (client type L) — validate, generate the pipe-delimited .UPL file from saved
/// legal-entity records and zip it. Mirrors <see cref="BuildZipCommand"/> for the
/// individual/retail client type, but operates on the dedicated legal-entity record tables
/// and marks the matching master rows with client type "L".
/// </summary>
public sealed class BuildZipLegalCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "build-zip-legal";
    public string Usage => "CKYCProcessor.exe build-zip-legal [--limit N]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var requestedLimit = OptionInt(args, "--limit") ?? CKYC.Core.Spec.CkycRecords.MaxLegalEntityBatchRecords;
        var limit = Math.Clamp(requestedLimit, 1, CKYC.Core.Spec.CkycRecords.MaxLegalEntityBatchRecords);
        var saved = await ctx.Master.GetByStatusAsync(MasterRecordStatus.Saved, limit, "L", ct);
        if (saved.Count == 0)
        {
            Log.Info("[build-zip-legal] No Saved legal-entity records to batch. Run `insert-legal` first.");
            return 0;
        }

        var customerIds = saved.Select(r => r.CustomerId).ToList();
        var entities = await ctx.LegalEntities.GetByCustomerIdsAsync(customerIds, ct);
        // O(n) lookup instead of an O(n²) FirstOrDefault scan per customer id.
        var byCustomerId = entities.ToDictionary(e => e.CustomerId, StringComparer.Ordinal);
        var ordered = customerIds
            .Where(byCustomerId.ContainsKey)
            .Select(id => byCustomerId[id])
            .ToList();

        if (ordered.Count == 0)
        {
            Log.Info("[build-zip-legal] No stored legal-entity records matched the saved master records.");
            return 0;
        }

        var batch = await ctx.LegalEntityBatchGenerator.GenerateAsync(ordered, DateOnly.FromDateTime(DateTime.Today), ct);

        var skippedIds = batch.Skipped is null ? new HashSet<string>() : new HashSet<string>(batch.Skipped.Select(s => s.CustomerId));
        var batchedRecords = saved.Where(r => !skippedIds.Contains(r.CustomerId)).ToList();
        var batchedIds = batchedRecords.Select(r => r.Id).ToList();

        var lineByRecord = new Dictionary<long, int>();
        if (batch.Record20Lines is not null)
            foreach (var r in batchedRecords)
                if (batch.Record20Lines.TryGetValue(r.CustomerId, out var line))
                    lineByRecord[r.Id] = line;

        await ctx.Master.MarkBatchAsync(batchedIds, batch.UploadFileName, lineByRecord, ct);
        await ctx.Journal.LogBatchAsync(batch, ct);

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
                Remarks = $"Batched legal entity into '{batch.UploadFileName}' at record-20 line {lineByRecord.GetValueOrDefault(r.Id)}",
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

        Log.Info("[build-zip-legal] Batch '{BatchKey}' generated with {RecordCount} legal entity record(s).", batch.BatchKey, batch.RecordCount);
        Log.Info("[build-zip-legal]   Upload file : {UploadFilePath}", batch.UploadFilePath);
        Log.Info("[build-zip-legal]   Zip archive : {ZipPath}", batch.ZipPath);

        if (batch.SkippedCount > 0)
        {
            Log.Warn("[build-zip-legal]   Skipped     : {SkippedCount} record(s) failed validation:", batch.SkippedCount);
            foreach (var s in batch.Skipped!)
            {
                Log.Warn("[build-zip-legal]     ! {CustomerId} ({CustomerName})", s.CustomerId, s.CustomerName);
                foreach (var e in s.Errors)
                    Log.Warn("[build-zip-legal]         - [{RecordType}/{FieldName}] {ErrorDescription}", e.RecordType, e.FieldName, e.ErrorDescription);
            }
        }
        else
        {
            Log.Info("[build-zip-legal]   Skipped     : none");
        }

        Log.Info("[build-zip-legal] Run `fvu` to submit this batch to the File Validation Utility.");
        return batch.SkippedCount > 0 ? 1 : 0;
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
    }
}
