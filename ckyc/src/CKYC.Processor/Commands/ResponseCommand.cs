using System.Text;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using CKYC.Files;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Reads the CERSAI response (reply) <c>*.UPL.RESm</c> files produced for a submitted batch,
/// records every detail against the owning master record in <c>master_record_response</c>,
/// and advances the master row (status → ResponseRead/Reconciled/Rejected + stage flags +
/// the <c>LastResponse*</c> summary columns).
///
/// Usage:
///   CKYCProcessor.exe response read                            # last batch, configured response folder
///   CKYCProcessor.exe response read --batch &lt;key&gt;           # a specific batch
///   CKYCProcessor.exe response read --dir &lt;folder&gt;            # scan a folder for .RES files
///   CKYCProcessor.exe response read --file &lt;path&gt;             # a single response file
/// </summary>
public sealed class ResponseCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "response";
    public string Usage => "CKYCProcessor.exe response read [--batch <key>] [--dir <folder>] [--file <path>]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var sub = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (sub is not null && !string.Equals(sub, "read", StringComparison.OrdinalIgnoreCase))
        {
            Log.Error("[response] Unknown sub-command '{Sub}'. Use: response read [...]", sub);
            return 1;
        }

        var batch = await ResolveBatchAsync(ctx, args, ct);
        if (batch is null)
        {
            Log.Warn("[response] No batch available. Run `build-zip` first.");
            return 1;
        }

        var files = ResolveResponseFiles(ctx, batch, args);
        if (files.Count == 0)
        {
            Log.Warn("[response] No response (.RES) files found for batch '{BatchKey}' (looked in '{Dir}'). Run the FVU to process the batch first.",
                batch.BatchKey, ResolveDir(ctx, batch, args));
            return 1;
        }

        Log.Info("[response] Reading {Count} response file(s) for batch '{BatchKey}' (upload: {UploadFileName})...", files.Count, batch.BatchKey, batch.UploadFileName);

        var processedFiles = 0;
        var alreadyImported = 0;
        var totalDetails = 0;
        var matched = 0;
        var unmatched = 0;
        var reconciled = 0;
        var rejected = 0;
        var responseActivity = await ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.ResponseRead, ct);

        foreach (var file in files)
        {
            var imports = await UploadResponseReader.ReadAsync(file, ctx.Hasher, ct);
            foreach (var import in imports)
            {
                var name = Path.GetFileName(import.ResponseFileName);
                var parsed = import.Parsed;
                var hdr = parsed.Header;
                if (await ctx.Master.HasUploadResponseFileAsync(import.SourceHash, name, ct))
                {
                    alreadyImported++;
                    Log.Info("[response]   {Name}: already imported", name);
                    continue;
                }
                processedFiles++;

                Log.Info("[response]   {Name} (resp #{ResponseFileNumber}) header: total={TotalRecords} processed={TotalProcessed} pending={UnderProcessing} failed={Failed} ts={ResponseTimestamp}",
                    name, parsed.ResponseFileNumber, hdr?.TotalRecords, hdr?.TotalProcessed, hdr?.UnderProcessing, hdr?.Failed, hdr?.ResponseTimestamp);

                foreach (var detail in parsed.Details)
                {
                totalDetails++;
                var master = await ResolveMasterAsync(ctx, batch, detail, ct);
                if (master is null)
                {
                    unmatched++;
                    Log.Warn("[response]     ! detail line {LineNumber} -> input line {InputRecordLineNumber} (status {Status}) no matching master record; skipped",
                        detail.LineNumber, detail.InputRecordLineNumber, detail.RecordStatus);
                    continue;
                }

                await ctx.Master.AddResponseAsync(new MasterRecordResponse
                {
                    MasterRecordId = master.Id,
                    CustomerId = master.CustomerId,
                    BatchFile = batch.UploadFileName,
                    ResponseFileNumber = parsed.ResponseFileNumber,
                    ResponseFileName = name,
                    LineNumber = detail.LineNumber,
                    InputRecordLineNumber = detail.InputRecordLineNumber,
                    AckNumber = detail.AckNumber,
                    RecordStatus = detail.RecordStatus,
                    CkycReferenceNumber = detail.CkycReferenceNumber,
                    CkycNumber = detail.CkycNumber,
                    RejectionRemark = detail.RejectionRemark,
                    ReadAt = DateTime.UtcNow,
                    Remarks = string.IsNullOrWhiteSpace(detail.RejectionRemark)
                        ? null
                        : $"Rejected by CERSAI: {detail.RejectionRemark}",
                    RawData = BuildRaw(detail),
                }, ct);
                matched++;
                var isCurrentBatch = string.Equals(master.BatchFile, batch.UploadFileName, StringComparison.OrdinalIgnoreCase);

                // Audit trail: every response detail read is an attempt in the "Response" stage,
                // so the history of response 0 / 1 / 2 is fully traceable.
                await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
                {
                    MasterRecordId = master.Id,
                    CustomerId = master.CustomerId,
                    Stage = "Response",
                    ActivityTypeId = responseActivity?.Id,
                    Status = (int)MasterRecordStatus.ResponseRead,
                    Success = true,
                    Remarks = $"resp#{parsed.ResponseFileNumber} ack={detail.AckNumber} status={detail.RecordStatus} ref={detail.CkycReferenceNumber} ckycNo={detail.CkycNumber}",
                    AttemptedAt = DateTime.UtcNow,
                }, ct);

                // Simple reconciliation rule driven by the reply: a rejection remark means the
                // record was rejected; a confirmed match (01) or no-match (02) mean reconciled.
                if (!isCurrentBatch)
                {
                    Log.Info("[response]     {CustomerId} historical response stored; current batch is {BatchFile}", master.CustomerId, master.BatchFile);
                }
                else if (IsRejected(detail.RecordStatus, detail.RejectionRemark))
                {
                    await ctx.Master.UpdateStatusAsync(master.Id, MasterRecordStatus.Rejected,
                        $"Rejected by CERSAI: {detail.RejectionRemark}", detail.RejectionRemark, ct);
                    rejected++;
                    Log.Warn("[response]     {CustomerId} REJECTED: {RejectionRemark}", master.CustomerId, detail.RejectionRemark);
                }
                else if (detail.RecordStatus is "01" or "02")
                {
                    await ctx.Master.UpdateStatusAsync(master.Id, MasterRecordStatus.Reconciled,
                        $"Response read - status {detail.RecordStatus} (ack {detail.AckNumber})",
                        null, ct);
                    reconciled++;
                    Log.Info("[response]     {CustomerId} reconciled status={Status} ack={AckNumber} ref={CkycReferenceNumber} ckycNo={CkycNumber}",
                        master.CustomerId, detail.RecordStatus, detail.AckNumber, detail.CkycReferenceNumber, detail.CkycNumber);
                }
                else
                {
                    Log.Info("[response]     {CustomerId} recorded status={Status} ack={AckNumber}", master.CustomerId, detail.RecordStatus, detail.AckNumber);
                }
                }

                await ctx.Master.TryAddUploadResponseFileAsync(new UploadResponseFile
                {
                    BatchFile = batch.UploadFileName,
                    ResponseFileName = name,
                    ResponseFileNumber = parsed.ResponseFileNumber,
                    TotalRecords = hdr?.TotalRecords ?? 0,
                    TotalProcessed = hdr?.TotalProcessed ?? 0,
                    UnderProcessing = hdr?.UnderProcessing ?? 0,
                    Failed = hdr?.Failed ?? 0,
                    ResponseTimestamp = hdr?.ResponseTimestamp,
                    RawHeaderData = import.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(line => line.StartsWith("90|", StringComparison.Ordinal)),
                    SourceArchiveName = import.SourceArchiveName,
                    SourceHash = import.SourceHash,
                }, ct);
            }
        }

        Log.Info("[response] Done: files={ProcessedFiles} already-imported={AlreadyImported} details={TotalDetails} matched={Matched} unmatched={Unmatched} reconciled={Reconciled} rejected={Rejected}",
            processedFiles, alreadyImported, totalDetails, matched, unmatched, reconciled, rejected);
        return alreadyImported > 0 || (processedFiles > 0 && unmatched == 0) ? 0 : 1;
    }

    private static async Task<GeneratedBatch?> ResolveBatchAsync(AppContext ctx, string[] args, CancellationToken ct)
    {
        var key = Option(args, "--batch");
        if (!string.IsNullOrWhiteSpace(key)) return await ctx.Journal.GetBatchByKeyAsync(key, ct);

        var file = Option(args, "--file");
        if (!string.IsNullOrWhiteSpace(file))
        {
            var uploadFile = UploadResponseReader.InferUploadFileName(file);
            if (!string.IsNullOrWhiteSpace(uploadFile))
                return await ctx.Journal.GetBatchByUploadFileAsync(uploadFile, ct);
        }

        return await ctx.Journal.GetLastBatchAsync(ct);
    }

    private static Task<Core.Domain.MasterRecord?> ResolveMasterAsync(AppContext ctx, GeneratedBatch batch, ResponseDetail detail, CancellationToken ct)
    {
        // The reply's "line number of input record of type 20" matches the record-20 line
        // stored on the master row (BatchRecordLine) at batch time.
        if (detail.InputRecordLineNumber is { } line)
            return ctx.Master.GetByBatchLineAsync(batch.UploadFileName, line, ct);

        // Fallback when the response does not carry the line number: only safe if the batch
        // holds a single record.
        var batched = ctx.Master.GetByBatchFileAsync(batch.UploadFileName, ct);
        return UnwrapSingle(batched);
    }

    private static async Task<Core.Domain.MasterRecord?> UnwrapSingle(Task<IReadOnlyList<Core.Domain.MasterRecord>> task)
    {
        var list = await task;
        return list.Count == 1 ? list[0] : null;
    }

    private static IReadOnlyList<string> ResolveResponseFiles(AppContext ctx, GeneratedBatch batch, string[] args)
    {
        var file = Option(args, "--file");
        if (file is not null) return File.Exists(file) ? new[] { file } : Array.Empty<string>();

        var dir = ResolveDir(ctx, batch, args);
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        var prefix = Path.GetFileNameWithoutExtension(batch.UploadFileName);
        return Directory.GetFiles(dir, "*.RES*")
            .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => CkycResponseParser.ParseResponseFileNumber(Path.GetFileName(f)))
            .ToList();
    }

    private static string ResolveDir(AppContext ctx, GeneratedBatch batch, string[] args)
        => Option(args, "--dir")
           ?? (string.IsNullOrWhiteSpace(ctx.Settings.Response.InputRoot)
               ? null
               : ctx.Settings.Response.InputRoot)
           ?? Path.Combine(ctx.Settings.Fvu.WorkspaceRoot, "runs", batch.BatchKey, "output");

    private static bool IsRejected(string? recordStatus, string? rejectionRemark)
        => !string.IsNullOrWhiteSpace(rejectionRemark)
           || (recordStatus is not null && recordStatus is not "01" and not "02");

    private static string BuildRaw(ResponseDetail d)
    {
        var sb = new StringBuilder();
        sb.Append("100|").Append(d.LineNumber).Append('|').Append(d.InputRecordLineNumber?.ToString() ?? "")
          .Append('|').Append(d.AckNumber ?? "").Append('|').Append(d.RecordStatus ?? "")
          .Append('|').Append(d.CkycReferenceNumber ?? "").Append('|').Append(d.CkycNumber ?? "")
          .Append('|').Append(d.RejectionRemark ?? "");
        return sb.ToString();
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
