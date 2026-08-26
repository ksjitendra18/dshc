using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Stage one: load search_customer.json into the search request table.</summary>
public sealed class SearchLoadCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "search-load";
    public string Usage => "CKYCProcessor.exe search-load [search_customer.json]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "search_customer.json";
        if (!File.Exists(path))
        {
            Log.Error("[search-load] Input file not found: {Path}", Path.GetFullPath(path));
            return 1;
        }
        var records = await SearchJsonReader.ReadAsync(path, ct);
        var result = await ctx.Search.InsertAsync(records, ct);
        Log.Info("[search-load] Inserted {Inserted} of {Total} record(s) into search_request.", result.Inserted, result.Total);
        return 0;
    }
}
