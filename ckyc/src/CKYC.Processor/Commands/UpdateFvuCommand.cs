using CKYC.Core.Models;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Stage three of the bulk-update pipeline: validate the latest generated .UPD through the
/// CERSAI FVU (same runner and simulation fallback the .UPL / .SRC paths use) and archive its
/// processed ZIP + hash on <c>update_batch</c>.
/// </summary>
public sealed class UpdateFvuCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "update-fvu";
    public string Usage => "CKYCProcessor.exe update-fvu [--file I_IAU010441_IN0238_DDMMYYYY_nnnnn.UPD]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var batch = await ctx.Updates.GetGeneratedBatchAsync(Option(args, "--file"), ct);
        if (batch is null)
        {
            Log.Error("[update-fvu] No generated .UPD file is awaiting validation. Run `update-process` first.");
            return 1;
        }
        if (!File.Exists(batch.FilePath))
        {
            const string message = "Generated .UPD file is missing from its recorded location.";
            await ctx.Updates.RecordFvuAsync(batch.Id, false, null, null, message, ct);
            Log.Error("[update-fvu] {Message} {FilePath}", message, batch.FilePath);
            return 1;
        }

        var generated = new GeneratedBatch(Path.GetFileNameWithoutExtension(batch.FileName), batch.FileName,
            batch.FilePath, null, batch.RecordCount, DateTime.UtcNow);
        Log.Info("[update-fvu] Validating {FileName} through FVU...", batch.FileName);
        var result = await ctx.Fvu.RunAsync(generated, ct);
        await ctx.Journal.LogFvuRunAsync(result, ct);

        string? finalZip = null;
        var passed = result.Passed;
        var error = result.ErrorMessage;
        try
        {
            if (passed)
            {
                if (string.IsNullOrWhiteSpace(result.OutputZipPath) || !File.Exists(result.OutputZipPath))
                    throw new InvalidDataException("FVU reported success but did not produce an output ZIP.");
                finalZip = batch.FilePath + ".zip";
                if (!string.Equals(Path.GetFullPath(result.OutputZipPath), Path.GetFullPath(finalZip), StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(finalZip)) throw new IOException($"Validated ZIP already exists: {finalZip}");
                    File.Copy(result.OutputZipPath, finalZip);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            passed = false;
            error = ex.Message;
        }

        await ctx.Updates.RecordFvuAsync(batch.Id, passed, finalZip, result.Hash, error, ct);
        Print(result, finalZip, passed, error);
        return passed ? 0 : 1;
    }

    private static void Print(FvuRunResult result, string? finalZip, bool passed, string? error)
    {
        Log.Info("[update-fvu] Executed={Executed} ExitCode={ExitCode} Passed={Passed}", result.Executed, result.ExitCode, passed);
        if (result.Summary is { } summary) Log.Info("[update-fvu] files={TotalFiles} success={Success} failed={Failed}", summary.TotalFiles, summary.Success, summary.Failed);
        if (finalZip is not null) Log.Info("[update-fvu] UPD ZIP: {FinalZip}", finalZip);
        if (result.Hash is not null) Log.Info("[update-fvu] Hash: {Hash}", result.Hash);
        if (error is not null) Log.Error("[update-fvu] Error: {Error}", error);
        foreach (var issue in result.ValidationErrors ?? Array.Empty<ValidationError>())
            Log.Error("[update-fvu] {LineNumber} {FieldName} [{ErrorCode}] {ErrorDescription}", issue.LineNumber, issue.FieldName, issue.ErrorCode, issue.ErrorDescription);
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
