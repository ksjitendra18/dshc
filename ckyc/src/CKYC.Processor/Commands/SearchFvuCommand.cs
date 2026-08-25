using CKYC.Core.Models;

namespace CKYC.Processor.Commands;

/// <summary>Stage three: validate the latest generated SRC through the CKYCR FVU.</summary>
public sealed class SearchFvuCommand : ICommand
{
    public string Name => "search-fvu";
    public string Usage => "CKYCProcessor.exe search-fvu [--file IRA000337_IN9797_DDMMYYYY_nnnnn.SRC]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var batch = await ctx.Search.GetGeneratedBatchAsync(Option(args, "--file"), ct);
        if (batch is null)
        {
            Console.Error.WriteLine("[search-fvu] No generated SRC file is awaiting validation. Run `search-process` first.");
            return 1;
        }
        if (!File.Exists(batch.FilePath))
        {
            const string message = "Generated SRC file is missing from its recorded location.";
            await ctx.Search.RecordFvuAsync(batch.Id, false, null, null, message, ct);
            Console.Error.WriteLine($"[search-fvu] {message} {batch.FilePath}");
            return 1;
        }

        var generated = new GeneratedBatch(Path.GetFileNameWithoutExtension(batch.FileName), batch.FileName,
            batch.FilePath, null, batch.RecordCount, DateTime.UtcNow);
        Console.WriteLine($"[search-fvu] Validating {batch.FileName} through FVU...");
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

        await ctx.Search.RecordFvuAsync(batch.Id, passed, finalZip, result.Hash, error, ct);
        Print(result, finalZip, passed, error);
        return passed ? 0 : 1;
    }

    private static void Print(FvuRunResult result, string? finalZip, bool passed, string? error)
    {
        Console.WriteLine($"[search-fvu] Executed={result.Executed} ExitCode={result.ExitCode} Passed={passed}");
        if (result.Summary is { } summary) Console.WriteLine($"[search-fvu] files={summary.TotalFiles} success={summary.Success} failed={summary.Failed}");
        if (finalZip is not null) Console.WriteLine($"[search-fvu] SRC ZIP: {finalZip}");
        if (result.Hash is not null) Console.WriteLine($"[search-fvu] Hash: {result.Hash}");
        if (error is not null) Console.Error.WriteLine($"[search-fvu] Error: {error}");
        foreach (var issue in result.ValidationErrors ?? Array.Empty<ValidationError>())
            Console.Error.WriteLine($"[search-fvu] {issue.LineNumber} {issue.FieldName} [{issue.ErrorCode}] {issue.ErrorDescription}");
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
