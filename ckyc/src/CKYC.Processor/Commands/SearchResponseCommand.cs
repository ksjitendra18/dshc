using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Imports CKYCR SRC response files (.SRC.RESm or a ZIP containing one).</summary>
public sealed class SearchResponseCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "search-response";
    public string Usage => "CKYCProcessor.exe search-response [--path runtime\\search\\response]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var path = Option(args, "--path") ?? Path.Combine(ctx.Settings.Search.OutputRoot, "response");
        var fullPath = Path.GetFullPath(path);
        var files = ResolveFiles(fullPath);
        if (files.Count == 0)
        {
            Log.Warn("[search-response] No .SRC.RES response files found in {Path}", fullPath);
            return 0;
        }

        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                var imports = await SearchResponseReader.ReadAsync(file, ctx.Hasher.ComputeSha256(file), ct);
                foreach (var response in imports)
                {
                    var result = await ctx.Search.ImportResponseAsync(response, ct);
                    var label = result.AlreadyImported ? "already imported" : $"stored={result.Inserted} matched={result.MatchedRequests}";
                    Log.Info("[search-response] {ResponseFile}: {Label}", response.Header.ResponseFileName, label);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                Log.Error(ex, "[search-response] {File}: {Message}", Path.GetFileName(file), ex.Message);
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private static List<string> ResolveFiles(string path)
    {
        if (File.Exists(path)) return new List<string> { path };
        if (!Directory.Exists(path)) return new List<string>();
        return Directory.GetFiles(path)
            .Where(file => Path.GetFileName(file).Contains(".SRC.RES", StringComparison.OrdinalIgnoreCase)
                        || (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                            && Path.GetFileName(file).Contains(".SRC", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
