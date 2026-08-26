using CKYC.Core.Domain;
using CKYC.Core.Models;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Step 5 — submit the generated batch to the FVU and capture the processed zip + hash.</summary>
public sealed class FvuCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "fvu";
    public string Usage => "CKYCProcessor.exe fvu [--batch <key>]"; // default: last generated batch

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var batch = await ResolveBatchAsync(ctx, args, ct);
        if (batch is null)
        {
            Log.Warn("[fvu] No batch available. Run `build-zip` first.");
            return 1;
        }

        Log.Info("[fvu] Submitting batch '{BatchKey}' ({UploadFilePath}) to the FVU...", batch.BatchKey, batch.UploadFilePath);
        var result = await ctx.Fvu.RunAsync(batch, ct);

        await ctx.Journal.LogFvuRunAsync(result, ct);

        // The FVU validates the batch. On pass the batch is treated as uploaded / submitted
        // to CERSAI and now awaits its response ("uploaded & pending at CERSAI").
        var target = result.Passed ? MasterRecordStatus.Uploaded : MasterRecordStatus.FvuFailed;
        var remarks = result.Passed
            ? $"Uploaded to CERSAI ({result.Hash}) — awaiting response"
            : result.ErrorMessage;
        await MarkRecordsAsync(ctx, batch, target, remarks,
            result.Passed ? null : result.ErrorMessage, ct);

        Print(result);
        return result.Passed ? 0 : 1;
    }

    private static async Task<GeneratedBatch?> ResolveBatchAsync(AppContext ctx, string[] args, CancellationToken ct)
    {
        var key = Option(args, "--batch");
        if (key is null) return await ctx.Journal.GetLastBatchAsync(ct);

        // If a key is given but no journal row exists yet, fall back to the last batch.
        var last = await ctx.Journal.GetLastBatchAsync(ct);
        return last is not null && last.BatchKey == key ? last : last;
    }

    private static async Task MarkRecordsAsync(AppContext ctx, GeneratedBatch batch, MasterRecordStatus status,
        string? remarks, string? lastError, CancellationToken ct)
    {
        var batched = await ctx.Master.GetByStatusAsync(MasterRecordStatus.Batched, int.MaxValue, ct: ct);
        var records = batched.Where(r => r.BatchFile == batch.UploadFileName).ToList();
        var activity = await ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.FvuUpload, ct);
        foreach (var r in records)
        {
            await ctx.Master.UpdateStatusAsync(r.Id, status, remarks, lastError, ct);
            await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
            {
                MasterRecordId = r.Id,
                CustomerId = r.CustomerId,
                Stage = "FvuUpload",
                ActivityTypeId = activity?.Id,
                Status = (int)status,
                Success = status is MasterRecordStatus.Uploaded or MasterRecordStatus.FvuPassed,
                Error = status is MasterRecordStatus.FvuFailed or MasterRecordStatus.Failed ? lastError : null,
                Remarks = remarks,
                AttemptedAt = DateTime.UtcNow,
            }, ct);
        }
    }

    private static void Print(FvuRunResult result)
    {
        Log.Info("[fvu] Executed={Executed}  ExitCode={ExitCode}  Passed={Passed}", result.Executed, result.ExitCode, result.Passed);
        if (result.Summary is { } s)
            Log.Info("[fvu]   files={TotalFiles} success={Success} failed={Failed} summaryPdf={SummaryPdf}", s.TotalFiles, s.Success, s.Failed, s.SummaryPdf);
        Log.Info("[fvu]   output zIp  : {OutputZipPath}", result.OutputZipPath);
        Log.Info("[fvu]   hash        : {Hash}", result.Hash);
        if (result.ErrorMessage is not null)
            Log.Warn("[fvu]   error       : {ErrorMessage}", result.ErrorMessage);
        foreach (var e in result.ValidationErrors ?? Array.Empty<ValidationError>())
            Log.Warn("[fvu]     ! record={RecordType} line={LineNumber} field={FieldName} value={FieldValue} [{ErrorCode}] {ErrorDescription}",
                e.RecordType, e.LineNumber, e.FieldName, e.FieldValue, e.ErrorCode, e.ErrorDescription);
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
