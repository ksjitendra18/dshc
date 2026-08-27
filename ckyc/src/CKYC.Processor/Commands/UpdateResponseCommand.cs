using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Stage four of the bulk-update pipeline: import CERSAI bulk-update reply files (.UPD.RESm or
/// a ZIP containing one) into <c>update_response_file</c> / <c>update_response</c> and stamp
/// each matched submission with its ack number, status (02 No Match / 03 Rejected) and remark.
///
/// Detail lines are matched back to submissions via the .UPD file name plus the response's
/// "Line Number of Record type 20" value.
/// </summary>
public sealed class UpdateResponseCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "update-response";
    public string Usage => "CKYCProcessor.exe update-response [--path runtime\\update\\response]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var path = Option(args, "--path") ?? Path.Combine(ctx.Settings.Update.OutputRoot, "response");
        var fullPath = Path.GetFullPath(path);
        var files = ResolveFiles(fullPath);
        if (files.Count == 0)
        {
            Log.Warn("[update-response] No .UPD.RES response files found in {Path}", fullPath);
            return 0;
        }

        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                var imports = await UpdateResponseReader.ReadAsync(file, ctx.Hasher.ComputeSha256(file), ct);
                foreach (var import in imports)
                {
                    var result = await ctx.Updates.ImportResponseAsync(import, ct);
                    var label = result.AlreadyImported ? "already imported" : $"stored={result.Inserted} matched={result.MatchedRequests}";
                    Log.Info("[update-response] {ResponseFile}: {Label}", import.Header.ResponseFileName, label);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                Log.Error(ex, "[update-response] {File}: {Message}", Path.GetFileName(file), ex.Message);
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private static List<string> ResolveFiles(string path)
    {
        if (File.Exists(path)) return new List<string> { path };
        if (!Directory.Exists(path)) return new List<string>();
        return Directory.GetFiles(path)
            .Where(file => Path.GetFileName(file).Contains(".UPD.RES", StringComparison.OrdinalIgnoreCase)
                        || (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                            && Path.GetFileName(file).Contains(".UPD", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
