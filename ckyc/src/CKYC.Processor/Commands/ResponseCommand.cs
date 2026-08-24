using System.Text;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using CKYC.Files;

namespace CKYC.Processor.Commands;

/// <summary>
/// Reads the CERSAI response (reply) <c>*.UPL.RESm</c> files produced for a submitted batch,
/// records every detail against the owning master record in <c>master_record_response</c>,
/// and advances the master row (status → ResponseRead/Reconciled/Rejected + stage flags +
/// the <c>LastResponse*</c> summary columns).
///
/// Usage:
///   CKYCProcessor.exe response read                            # last batch, default output folder
///   CKYCProcessor.exe response read --batch &lt;key&gt;           # a specific batch
///   CKYCProcessor.exe response read --dir &lt;folder&gt;            # scan a folder for .RES files
///   CKYCProcessor.exe response read --file &lt;path&gt;             # a single response file
/// </summary>
public sealed class ResponseCommand : ICommand
{
    public string Name => "response";
    public string Usage => "CKYCProcessor.exe response read [--batch <key>] [--dir <folder>] [--file <path>]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var sub = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (sub is not null && !string.Equals(sub, "read", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"[response] Unknown sub-command '{sub}'. Use: response read [...]");
            return 1;
        }

        var batch = await ResolveBatchAsync(ctx, args, ct);
        if (batch is null)
        {
            Console.WriteLine("[response] No batch available. Run `build-zip` first.");
            return 1;
        }

        var files = ResolveResponseFiles(ctx, batch, args);
        if (files.Count == 0)
        {
            Console.WriteLine($"[response] No response (.RES) files found for batch '{batch.BatchKey}' " +
                              $"(looked in '{ResolveDir(ctx, batch, args)}'). Run the FVU to process the batch first.");
            return 1;
        }

        Console.WriteLine($"[response] Reading {files.Count} response file(s) for batch '{batch.BatchKey}' (upload: {batch.UploadFileName})...");

        var processedFiles = 0;
        var totalDetails = 0;
        var matched = 0;
        var unmatched = 0;
        var reconciled = 0;
        var rejected = 0;
        var responseActivity = await ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.ResponseRead, ct);

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var content = await File.ReadAllTextAsync(file, ct);
            var parsed = CkycResponseParser.Parse(name, content);
            processedFiles++;

            var hdr = parsed.Header;
            Console.WriteLine($"[response]   {name} (resp #{parsed.ResponseFileNumber})" +
                              $" header: total={hdr?.TotalRecords} processed={hdr?.TotalProcessed} " +
                              $"pending={hdr?.UnderProcessing} failed={hdr?.Failed} ts={hdr?.ResponseTimestamp}");

            foreach (var detail in parsed.Details)
            {
                totalDetails++;
                var master = await ResolveMasterAsync(ctx, batch, detail, ct);
                if (master is null)
                {
                    unmatched++;
                    Console.WriteLine($"[response]     ! detail line {detail.LineNumber} -> input line {detail.InputRecordLineNumber} (status {detail.RecordStatus}) no matching master record; skipped");
                    continue;
                }

                await ctx.Master.AddResponseAsync(new MasterRecordResponse
                {
                    MasterRecordId = master.Id,
                    SourceCustomerId = master.SourceCustomerId,
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

                // Audit trail: every response detail read is an attempt in the "Response" stage,
                // so the history of response 0 / 1 / 2 is fully traceable.
                await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
                {
                    MasterRecordId = master.Id,
                    SourceCustomerId = master.SourceCustomerId,
                    Stage = "Response",
                    ActivityTypeId = responseActivity?.Id,
                    Status = (int)MasterRecordStatus.ResponseRead,
                    Success = true,
                    Remarks = $"resp#{parsed.ResponseFileNumber} ack={detail.AckNumber} status={detail.RecordStatus} ref={detail.CkycReferenceNumber} ckycNo={detail.CkycNumber}",
                    AttemptedAt = DateTime.UtcNow,
                }, ct);

                // Simple reconciliation rule driven by the reply: a rejection remark means the
                // record was rejected; a confirmed match (01) or no-match (02) mean reconciled.
                if (IsRejected(detail.RecordStatus, detail.RejectionRemark))
                {
                    await ctx.Master.UpdateStatusAsync(master.Id, MasterRecordStatus.Rejected,
                        $"Rejected by CERSAI: {detail.RejectionRemark}", detail.RejectionRemark, ct);
                    rejected++;
                    Console.WriteLine($"[response]     {master.SourceCustomerId} REJECTED: {detail.RejectionRemark}");
                }
                else if (detail.RecordStatus is "01" or "02")
                {
                    await ctx.Master.UpdateStatusAsync(master.Id, MasterRecordStatus.Reconciled,
                        $"Response read - status {detail.RecordStatus} (ack {detail.AckNumber})",
                        null, ct);
                    reconciled++;
                    Console.WriteLine($"[response]     {master.SourceCustomerId} reconciled status={detail.RecordStatus} " +
                                      $"ack={detail.AckNumber} ref={detail.CkycReferenceNumber} ckycNo={detail.CkycNumber}");
                }
                else
                {
                    Console.WriteLine($"[response]     {master.SourceCustomerId} recorded status={detail.RecordStatus} ack={detail.AckNumber}");
                }
            }
        }

        Console.WriteLine($"[response] Done: files={processedFiles} details={totalDetails} " +
                          $"matched={matched} unmatched={unmatched} reconciled={reconciled} rejected={rejected}");
        return matched > 0 ? 0 : 1;
    }

    private static Task<GeneratedBatch?> ResolveBatchAsync(AppContext ctx, string[] args, CancellationToken ct)
    {
        var key = Option(args, "--batch");
        // Default to the last generated batch, or the batch with the matching key.
        return ctx.Journal.GetLastBatchAsync(ct);
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
