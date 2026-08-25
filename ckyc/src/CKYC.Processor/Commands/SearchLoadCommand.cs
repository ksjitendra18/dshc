namespace CKYC.Processor.Commands;

/// <summary>Stage one: load search_customer.json into the search request table.</summary>
public sealed class SearchLoadCommand : ICommand
{
    public string Name => "search-load";
    public string Usage => "CKYCProcessor.exe search-load [search_customer.json]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "search_customer.json";
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[search-load] Input file not found: {Path.GetFullPath(path)}");
            return 1;
        }
        var records = await SearchJsonReader.ReadAsync(path, ct);
        var result = await ctx.Search.InsertAsync(records, ct);
        Console.WriteLine($"[search-load] Inserted {result.Inserted} of {result.Total} record(s) into search_request.");
        return 0;
    }
}
