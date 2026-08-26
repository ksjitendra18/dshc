using System.Globalization;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Stage two: atomically claim pending rows and generate one SRC file.</summary>
public sealed class SearchProcessCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "search-process";
    public string Usage => "CKYCProcessor.exe search-process [--limit N] [--date yyyy-MM-dd]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var businessDate = OptionDate(args, "--date") ?? DateOnly.FromDateTime(DateTime.Today);
        var timeout = TimeSpan.FromMinutes(Math.Max(1, ctx.Settings.Search.ClaimTimeoutMinutes));
        var claim = await ctx.Search.ClaimAsync(limit, businessDate, ctx.Settings.Search.SequenceStart, timeout, ct);
        if (claim is null)
        {
            Log.Info("[search-process] No pending search records.");
            return 0;
        }

        try
        {
            var content = ctx.SearchFileWriter.Write(claim.Records, businessDate);
            var fileName = ctx.SearchFileWriter.BuildFileName(businessDate, claim.FileSequence);
            var outputRoot = Path.GetFullPath(ctx.Settings.Search.OutputRoot);
            Directory.CreateDirectory(outputRoot);
            var path = Path.Combine(outputRoot, fileName);
            var temporaryPath = path + "." + claim.Token + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, content, ct);
            File.Move(temporaryPath, path, overwrite: false);
            await ctx.Search.CompleteAsync(claim, fileName, path, ct);
            Log.Info("[search-process] Created {Path} with {Count} detail record(s).", path, claim.Records.Count);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ctx.Search.FailAsync(claim, ex.Message, ct);
            Log.Error(ex, "[search-process] Failed claim {Token}: {Message}", claim.Token, ex.Message);
            return 1;
        }
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var value) ? value : null;
    }

    private static DateOnly? OptionDate(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0 || i + 1 >= args.Length) return null;
        if (DateOnly.TryParseExact(args[i + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value)) return value;
        throw new ArgumentException($"{name} must use yyyy-MM-dd format.");
    }
}
