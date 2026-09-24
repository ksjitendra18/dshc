using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// CERSAI SFTP transport over the vendor <c>SFTPRunner.exe</c>, split into two processes:
/// <list type="bullet">
///   <item><description><c>sftp push</c> — upload the FVU-validated <c>.UPL</c> batches staged in
///   the deterministic individual/legal outbound folders, then mark their records Uploaded.</description></item>
///   <item><description><c>sftp pull</c> — download the processed response files into the
///   configured download folder for later ingestion (<c>response read</c> / <c>download-response</c>).</description></item>
/// </list>
/// </summary>
public sealed class SftpCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "sftp";
    public string Usage => "CKYCProcessor.exe sftp <push|pull>";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var sub = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        return sub?.ToLowerInvariant() switch
        {
            "push" or "upload" => await PushAsync(ctx, ct),
            "pull" or "download" => await PullAsync(ctx, ct),
            _ => UnknownSub(sub),
        };
    }

    private static int UnknownSub(string? sub)
    {
        Log.Error("[sftp] Unknown sub-command '{Sub}'. Use: sftp push | sftp pull", sub);
        return 1;
    }

    // ---------------------------------------------------------------------
    // push: upload validated batches, mark records Uploaded, archive payloads
    // ---------------------------------------------------------------------
    private static async Task<int> PushAsync(AppContext ctx, CancellationToken ct)
    {
        if (!EnsureEnabled(ctx)) return 1;

        var paths = ctx.Sftp.Paths;
        var staged = paths.ListUploadPayloads();
        if (staged.Count == 0)
        {
            Log.Warn("[sftp push] No validated .UPL.zip files are staged. Run `fvu` first (looked in '{Individual}' and '{Legal}').",
                paths.IndividualFolder, paths.LegalEntityFolder);
            return 0;
        }

        Log.Info("[sftp push] Pushing {Count} validated batch file(s) to CERSAI...", staged.Count);
        foreach (var file in staged)
            Log.Info("[sftp push]   {Entity,-13} {FileName}", file.Entity, file.FileName);

        var result = await ctx.Sftp.UploadAsync(ct);
        Print(result);
        if (!result.Passed) return 1;

        await MarkUploadedAsync(ctx, staged, ct);
        if (ctx.Settings.Sftp.ArchiveUploadedFiles) Archive(paths, staged);

        Log.Info("[sftp push] Done: {Count} batch(es) submitted; awaiting the CERSAI response (`sftp pull`).", staged.Count);
        return 0;
    }

    private static async Task MarkUploadedAsync(AppContext ctx, IReadOnlyList<SftpStagedFile> staged, CancellationToken ct)
    {
        var activity = await ctx.Master.GetActivityTypeByCodeAsync(ActivityTypeCodes.SftpUpload, ct);
        var marked = 0;

        foreach (var file in staged)
        {
            // <uploadFileName>.zip -> <uploadFileName> (e.g. I_..._00058.UPL.zip -> I_..._00058.UPL).
            var uploadFileName = Path.GetFileNameWithoutExtension(file.FileName);
            var records = await ctx.Master.GetByBatchFileAsync(uploadFileName, ct);
            if (records.Count == 0)
            {
                Log.Warn("[sftp push]   {FileName}: no master records reference batch '{Batch}'.", file.FileName, uploadFileName);
                continue;
            }

            foreach (var record in records)
            {
                if (record.Status == MasterRecordStatus.Uploaded) continue;

                await ctx.Master.UpdateStatusAsync(record.Id, MasterRecordStatus.Uploaded,
                    $"Pushed to CERSAI via SFTP ({file.FileName}) — awaiting response", null, ct);
                await ctx.Master.LogAttemptAsync(new MasterRecordAttempt
                {
                    MasterRecordId = record.Id,
                    CustomerId = record.CustomerId,
                    Stage = "SftpUpload",
                    ActivityTypeId = activity?.Id,
                    Status = (int)MasterRecordStatus.Uploaded,
                    Success = true,
                    Remarks = $"Uploaded '{file.FileName}' ({file.Entity})",
                    AttemptedAt = DateTime.UtcNow,
                }, ct);
                marked++;
            }
        }

        Log.Info("[sftp push]   Marked {Count} record(s) as Uploaded.", marked);
    }

    private static void Archive(SftpPaths paths, IReadOnlyList<SftpStagedFile> staged)
    {
        foreach (var group in staged.GroupBy(f => f.Entity))
        {
            var destination = paths.ArchiveFolderFor(group.Key);
            Directory.CreateDirectory(destination);
            foreach (var file in group)
            {
                var target = Path.Combine(destination, file.FileName);
                if (File.Exists(target)) File.Delete(target);
                File.Move(file.Path, target);
                Log.Info("[sftp push]   Archived {FileName} -> {Destination}", file.FileName, destination);
            }
        }
    }

    // ---------------------------------------------------------------------
    // pull: download processed response files only (ingest separately)
    // ---------------------------------------------------------------------
    private static async Task<int> PullAsync(AppContext ctx, CancellationToken ct)
    {
        if (!EnsureEnabled(ctx)) return 1;

        var paths = ctx.Sftp.Paths;
        Directory.CreateDirectory(paths.DownloadFolder);
        Log.Info("[sftp pull] Downloading processed response files to '{Folder}'...", paths.DownloadFolder);

        var result = await ctx.Sftp.DownloadAsync(ct);
        Print(result);
        if (!result.Passed) return 1;

        Log.Info("[sftp pull] Downloaded {Count} new file(s).", result.Files.Count);
        foreach (var file in result.Files)
            Log.Info("[sftp pull]   {FileName}", Path.GetFileName(file));

        if (result.Files.Count > 0)
            Log.Info("[sftp pull] Next: `response read --dir \"{Folder}\"` (or `download-response --path` for .DWN.RES files).",
                paths.DownloadFolder);
        return 0;
    }

    // ---------------------------------------------------------------------
    private static bool EnsureEnabled(AppContext ctx)
    {
        if (ctx.Settings.Sftp.Enabled) return true;
        Log.Error("[sftp] SFTP integration is disabled. Set `sftp.enabled=true` in appsettings.json.");
        return false;
    }

    private static void Print(SftpRunResult result)
    {
        Log.Info("[sftp {Mode}] Executed={Executed} ExitCode={ExitCode} Passed={Passed}",
            result.Operation == SftpOperation.Upload ? "push" : "pull", result.Executed, result.ExitCode, result.Passed);
        if (result.ConfigPath is not null) Log.Info("[sftp]   config : {ConfigPath}", result.ConfigPath);
        if (result.ReportPath is not null) Log.Info("[sftp]   report : {ReportPath}", result.ReportPath);
        if (result.ErrorMessage is not null) Log.Warn("[sftp]   error  : {ErrorMessage}", result.ErrorMessage);
    }
}
