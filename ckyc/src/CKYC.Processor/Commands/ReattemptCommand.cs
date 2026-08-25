using CKYC.Core.Domain;

namespace CKYC.Processor.Commands;

/// <summary>
/// Re-push (reattempt) <b>one</b> record that was rejected by CERSAI due to a minor issue and
/// has since been corrected directly in the backend database. The processor:
/// <list type="number">
///   <item>snapshots the <b>previous attempt/response</b> (outcome, ack, CKYC ref/number,
///        rejection remark and the read date/timestamp) into <c>master_record_reattempt</c></item>
///   <item>flips the record's flag back to a re-pushable stage (Saved) and clears its rejection
///        flag / retry budget, so it flows through <c>build-zip</c> → <c>fvu</c> → <c>response read</c> again.</item>
/// </list>
///
/// Usage:
///   CKYCProcessor.exe reattempt --id 42 --reason "PAN corrected in backend"
///   CKYCProcessor.exe reattempt --customer CUST202608240001
/// </summary>
public sealed class ReattemptCommand : ICommand
{
    public string Name => "reattempt";
    public string Usage => "CKYCProcessor.exe reattempt --id <recordId> | --customer <customerId> [--reason \"...\"]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var record = await ResolveRecordAsync(ctx, args, ct);
        if (record is null)
        {
            Console.Error.WriteLine("[reattempt] Record not found. Pass --id <recordId> or --customer <customerId>.");
            return 1;
        }

        if (record.Status is not (MasterRecordStatus.Rejected or MasterRecordStatus.Failed) && !record.IsRejected)
        {
            Console.Error.WriteLine($"[reattempt] Record {record.CustomerId} is not in a re-pushable state (current status: {record.Status.Label()}).");
            return 1;
        }

        var reason = Option(args, "--reason") ?? "Manual backend fix; re-pushing corrected record";
        var now = DateTime.UtcNow;
        var reattemptCount = record.ReattemptCount + 1;

        // 1) Snapshot the previous attempt/response history BEFORE we reset anything.
        var snapshot = await SnapshotAsync(ctx, record, reason, reattemptCount, now, ct);
        await ctx.Master.LogReattemptAsync(snapshot, ct);

        // 2) Flip the record so it is re-pushable again.
        var remarks = $"Reattempt #{reattemptCount}: {reason}";
        await ctx.Master.ResetForReattemptAsync(record.Id, remarks, ct);

        // 3) Audit trail.
        await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
        {
            MasterRecordId = record.Id,
            CustomerId = record.CustomerId,
            Stage = "Reattempt",
            Status = (int)MasterRecordStatus.Saved,
            Success = true,
            Remarks = remarks,
            AttemptedAt = now,
        }, ct);

        Console.WriteLine($"[reattempt] Re-pushing {record.CustomerId} (record #{record.Id})");
        Console.WriteLine($"[reattempt]   prior status={record.Status.Label()} reconStatus={record.ReconStatus} retryCount={record.RetryCount}");
        if (record.LastResponseRejectionRemark is not null)
            Console.WriteLine($"[reattempt]   prior rejection remark: {record.LastResponseRejectionRemark}");
        Console.WriteLine($"[reattempt]   previous response snapshotted to master_record_reattempt; record reset to Saved.");
        Console.WriteLine("[reattempt] Next: `build-zip` to re-batch, then `fvu` and `response read`.");
        return 0;
    }

    private static async Task<MasterRecord?> ResolveRecordAsync(AppContext ctx, string[] args, CancellationToken ct)
    {
        var idText = Option(args, "--id");
        var customer = Option(args, "--customer");
        if (customer is not null)
        {
            var byCustomer = await ctx.Master.GetByCustomerIdsAsync(new[] { customer }, ct);
            return byCustomer.Count > 0 ? byCustomer[0] : null;
        }
        if (idText is not null && long.TryParse(idText, out var id))
            return await ctx.Master.GetByIdAsync(id, ct);
        return null;
    }

    /// <summary>Captures the "before" state — the most recent response/attempt history — into a reattempt snapshot.</summary>
    private static async Task<MasterRecordReattempt> SnapshotAsync(AppContext ctx, MasterRecord record, string reason, int reattemptCount, DateTime now, CancellationToken ct)
    {
        // Prefer the most granular response detail (master_record_response), fall back to the
        // master row's LastResponse* summary columns.
        var responses = await ctx.Master.GetResponsesAsync(record.Id, ct);
        var latest = responses.Count > 0 ? responses[^1] : null;

        return new MasterRecordReattempt
        {
            MasterRecordId = record.Id,
            CustomerId = record.CustomerId,
            Reason = reason,
            PreviousStatus = (int)record.Status,
            PreviousReconStatus = record.ReconStatus,
            PreviousResponseStatus = latest?.RecordStatus ?? record.LastResponseStatus,
            PreviousResponseAckNumber = latest?.AckNumber ?? record.LastResponseAckNumber,
            PreviousResponseCkycReference = latest?.CkycReferenceNumber ?? record.LastResponseCkycReference,
            PreviousResponseCkycNumber = latest?.CkycNumber ?? record.LastResponseCkycNumber,
            PreviousResponseRejectionRemark = latest?.RejectionRemark ?? record.LastResponseRejectionRemark,
            PreviousResponseReadAt = latest?.ReadAt ?? record.LastResponseReadAt,
            PreviousRetryCount = record.RetryCount,
            ReattemptCount = reattemptCount,
            ReattemptedAt = now,
        };
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
