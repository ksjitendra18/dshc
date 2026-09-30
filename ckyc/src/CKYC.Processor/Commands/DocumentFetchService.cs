using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using CKYC.Files.Documents;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Step 3b — pull each individual record's supporting image/document from its intake channel's
/// source (for <c>beckyc</c>: the SFTP folder <c>&lt;basePath&gt;/&lt;dockey&gt;</c>) and import it
/// into the document store.
/// <para>
/// The document key (<c>dockey</c>) arrives with the step-1 source fetch. Fetched files are
/// published under the target name (configured, or the record slot's current file name, or the
/// remote name) and the record slot is repointed at it, so <c>build-zip</c> finds the file.
/// </para>
/// A record whose image cannot be found is left in <see cref="MasterRecordStatus.ImageFailed"/>
/// (retryable via the <c>ImageFetch</c> activity) and can never reach the batch step.
/// </summary>
public sealed class DocumentFetchService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly AppContext _ctx;

    public DocumentFetchService(AppContext ctx) => _ctx = ctx;

    public async Task<SaveBatchResult> ProcessAsync(IReadOnlyList<MasterRecord> records, CancellationToken ct = default)
    {
        var success = 0;
        var failure = 0;

        foreach (var record in records)
        {
            try
            {
                if (await FetchAsync(record, ct)) success++;
                else failure++;
            }
            catch (Exception ex)
            {
                await FailAsync(record, ex.Message, ct);
                failure++;
            }
        }

        return new SaveBatchResult(success, failure, records.Count);
    }

    private async Task<bool> FetchAsync(MasterRecord record, CancellationToken ct)
    {
        var channel = MasterRecordSourceValue.For(record.Source);
        var channelSettings = _ctx.Settings.DocumentFetch.ForChannel(channel);
        var source = _ctx.DocumentSources.Resolve(channel);

        if (source is null || channelSettings is null)
        {
            // No image source for this channel — treat the step as a pass-through so the
            // record is not blocked (the app channel has no configured source yet).
            await AdvanceAsync(record, $"No image source configured for channel '{channel}'; image step skipped", ct);
            Log.Info("[documents] [{CustomerId}] channel '{Channel}' has no image source -> pass-through to PendingSearch", record.CustomerId, channel);
            return true;
        }

        var individual = (await _ctx.Individuals.GetByCustomerIdsAsync([record.CustomerId], ct)).SingleOrDefault();
        if (individual is null)
        {
            await FailAsync(record, "No stored individual record exists for the customer", ct);
            return false;
        }

        var key = string.IsNullOrWhiteSpace(record.DocumentKey) ? record.CustomerId : record.DocumentKey!;
        var fetched = await source.FetchAsync(new DocumentFetchRequest(channel, record.CustomerId, key), ct);

        var imported = new List<string>();
        var slotsChanged = false;
        foreach (var document in fetched)
        {
            var entry = ResolveEntry(channelSettings, document.RemoteName);
            var target = ResolveTarget(entry, individual, document.RemoteName);
            await using (var content = new MemoryStream(document.Content, writable: false))
            {
                await _ctx.IndividualDocuments.ImportAsync(
                    new DocumentImport(record.Id, target, channel, "Sftp", document.SourceReference), content, ct);
            }
            imported.Add(target);

            if (!string.IsNullOrWhiteSpace(entry?.Slot))
            {
                var warnings = new List<string>();
                RecordDocumentSlots.Apply(individual, [entry!.Slot!], target, warnings);
                foreach (var warning in warnings) Log.Warn("[documents] [{CustomerId}] {Warning}", record.CustomerId, warning);
                slotsChanged = true;
            }
        }

        if (slotsChanged)
        {
            var save = await _ctx.Individuals.SaveAsync(individual, ct);
            if (!save.Success)
            {
                await FailAsync(record, save.Error ?? "Failed to update record document references", ct);
                return false;
            }
        }

        var summary = $"Image/document fetched from {channel} ({string.Join(", ", imported)})";
        await AdvanceAsync(record, summary, ct);
        Log.Info("[documents] [{CustomerId}] fetched {Count} document(s) from {Channel}: {Files} -> PendingSearch",
            record.CustomerId, imported.Count, channel, string.Join(", ", imported));
        return true;
    }

    /// <summary>Finds the configured entry that selected a remote file (exact name, else the single entry).</summary>
    private static ChannelDocumentSettings? ResolveEntry(ChannelDocumentFetchSettings channel, string remoteName)
    {
        var enabled = channel.Documents.Where(entry => entry.Enabled).ToList();
        if (enabled.Count == 0) return null;

        var exact = enabled.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Remote)
            && string.Equals(entry.Remote.Trim(), remoteName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return enabled.Count == 1 ? enabled[0] : null;
    }

    private static string ResolveTarget(ChannelDocumentSettings? entry, Individual individual, string remoteName)
    {
        var target = ResolveConfiguredTarget(entry, individual) ?? remoteName;
        return AlignExtension(target, remoteName);
    }

    private static string? ResolveConfiguredTarget(ChannelDocumentSettings? entry, Individual individual)
    {
        if (!string.IsNullOrWhiteSpace(entry?.Target)) return entry!.Target!.Trim();
        if (!string.IsNullOrWhiteSpace(entry?.Slot))
        {
            var current = RecordDocumentSlots.Resolve(individual, entry!.Slot!);
            if (!string.IsNullOrWhiteSpace(current)) return current;
        }
        return null;
    }

    private static readonly string[] DocumentExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    /// <summary>
    /// Keeps the configured target's base name but forces its extension to match the fetched
    /// content when the two disagree — e.g. a beckyc base64 <c>.txt</c> whose bytes are sniffed as
    /// JPEG/PNG after the config guessed a different extension. The store validates the extension
    /// against the content signature, so the extension is the authoritative part.
    /// </summary>
    private static string AlignExtension(string target, string remoteName)
    {
        var targetExtension = Path.GetExtension(target);
        var contentExtension = Path.GetExtension(remoteName);
        if (string.IsNullOrEmpty(targetExtension) || string.IsNullOrEmpty(contentExtension)) return target;
        if (string.Equals(targetExtension, contentExtension, StringComparison.OrdinalIgnoreCase)) return target;
        if (!DocumentExtensions.Contains(contentExtension, StringComparer.OrdinalIgnoreCase)
            || !DocumentExtensions.Contains(targetExtension, StringComparer.OrdinalIgnoreCase)) return target;
        return Path.ChangeExtension(target, contentExtension);
    }

    private async Task AdvanceAsync(MasterRecord record, string remarks, CancellationToken ct)
    {
        await _ctx.Master.MarkImageFetchedAsync(record.Id, remarks, ct);
        await _ctx.Master.ClearRetryStateAsync(record.Id, ct);
        await LogAttemptAsync(record, MasterRecordStatus.PendingSearch, true, null, remarks, ct);
    }

    private async Task FailAsync(MasterRecord record, string error, CancellationToken ct)
    {
        var activity = await _ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.ImageFetch, ct);
        var attempt = record.RetryCount + 1;
        var retryable = activity is { IsRetryable: true };
        var nextRetryAt = retryable ? DateTime.UtcNow.AddHours(activity!.BackoffHoursAfter(attempt)) : (DateTime?)null;
        var exhausted = retryable && activity!.IsExhausted(attempt);

        await _ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.ImageFailed, null, error, ct);
        await _ctx.Master.RecordRetryAsync(record.Id, attempt, error, ActivityTypeCodes.ImageFetch, nextRetryAt, exhausted, ct);
        await LogAttemptAsync(record, MasterRecordStatus.ImageFailed, false, error,
            $"retry {attempt}{(exhausted ? " (budget exhausted -> reconcile)" : "")}", ct, activity?.Id, nextRetryAt);

        if (exhausted)
            Log.Warn("[documents] [{CustomerId}] image fetch FAILED (retry {Attempt}): {Error} -> blocked from batch, flagged for reconciliation", record.CustomerId, attempt, error);
        else
            Log.Warn("[documents] [{CustomerId}] image fetch FAILED (retry {Attempt}): {Error} -> blocked from batch, next retry {NextRetryAt:u}", record.CustomerId, attempt, error, nextRetryAt);
    }

    private Task<int> LogAttemptAsync(MasterRecord record, MasterRecordStatus status, bool success,
        string? error, string? remarks, CancellationToken ct, long? activityTypeId = null, DateTime? nextRetryAt = null)
        => _ctx.Master.LogAttemptAsync(new MasterRecordAttempt
        {
            MasterRecordId = record.Id,
            CustomerId = record.CustomerId,
            Stage = ActivityTypeCodes.ImageFetch,
            ActivityTypeId = activityTypeId,
            Status = (int)status,
            Success = success,
            Error = error,
            Remarks = remarks,
            AttemptedAt = DateTime.UtcNow,
            NextRetryAt = nextRetryAt,
        }, ct);
}
