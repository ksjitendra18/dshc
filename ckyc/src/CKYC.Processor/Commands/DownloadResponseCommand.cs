using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Imports CKYCR .DWN.RES ZIP snapshots, record lines and document inventory.</summary>
public sealed class DownloadResponseCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "download-response";
    public string Usage => "CKYCProcessor.exe download-response --path <file-or-folder>";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var path = Option(args, "--path");
        if (string.IsNullOrWhiteSpace(path))
        {
            Log.Error("[download-response] Pass --path <DWN.RES zip/file/folder>.");
            return 1;
        }

        var fullPath = Path.GetFullPath(path);
        string[] files = File.Exists(fullPath) ? [fullPath] : Directory.Exists(fullPath)
            ? Directory.GetFiles(fullPath).Where(f => Path.GetFileName(f).Contains(".DWN.RES", StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];
        if (files.Length == 0)
        {
            Log.Warn("[download-response] No .DWN.RES files found at {Path}", fullPath);
            return 1;
        }

        var failures = 0;
        foreach (var file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var response in await DownloadResponseReader.ReadAsync(file, ctx.Hasher, ct))
                {
                    var result = await ctx.Downloads.ImportAsync(response, ct);
                    var label = result.AlreadyImported ? "already imported" : $"lines={result.Lines} artifacts={result.Artifacts}";
                    Log.Info("[download-response] {ResponseFile}: {Label}", response.ResponseFileName, label);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                Log.Error(ex, "[download-response] {File}: {Message}", Path.GetFileName(file), ex.Message);
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
