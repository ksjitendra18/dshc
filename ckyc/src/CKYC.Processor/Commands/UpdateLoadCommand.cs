using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Stage one of the bulk-update pipeline: parse an update-intake JSON file and insert the
/// submissions as pending <c>update_request</c> rows. Each record names an existing CKYC
/// number plus the sections being amended (see <c>vendor/individual-format-update.xlsx</c>
/// and <c>vendor/legal-format-update.xlsx</c>).
/// </summary>
public sealed class UpdateLoadCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "update-load";
    public string Usage => "CKYCProcessor.exe update-load [updates.json]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "update_customer.json";
        if (!File.Exists(path))
        {
            Log.Error("[update-load] Input file not found: {Path}", Path.GetFullPath(path));
            return 1;
        }
        var records = await UpdateJsonReader.ReadAsync(path, ct);
        var result = await ctx.Updates.InsertAsync(records, ct);
        var individuals = records.Count(r => r.ClientType == "I");
        var legalEntities = records.Count - individuals;
        Log.Info("[update-load] Inserted {Inserted} of {Total} submission(s) into update_request ({Individuals} individual, {Legal} legal entity).",
            result.Inserted, result.Total, individuals, legalEntities);
        Log.Info("[update-load] Next: `update-process` to generate the .UPD batch.");
        return 0;
    }
}
