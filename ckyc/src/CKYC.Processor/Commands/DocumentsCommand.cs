using CKYC.Core.Domain;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>Imports customer documents from a local/staging directory into the database.</summary>
public sealed class DocumentsCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "documents";
    public string Usage => "CKYCProcessor.exe documents import --customer-id <id> --dir <path>";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0 || !string.Equals(args[0], "import", StringComparison.OrdinalIgnoreCase))
        {
            Log.Error("[documents] Expected: {Usage}", Usage);
            return 1;
        }

        var customerId = Option(args, "--customer-id");
        var directory = Option(args, "--dir");
        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(directory))
        {
            Log.Error("[documents] Both --customer-id and --dir are required.");
            return 1;
        }
        if (!Directory.Exists(directory))
        {
            Log.Error("[documents] Staging directory does not exist: {Directory}", Path.GetFullPath(directory));
            return 1;
        }

        var masters = await ctx.Master.GetByCustomerIdsAsync([customerId], ct);
        if (masters.Count == 0)
        {
            Log.Error("[documents] Customer '{CustomerId}' does not exist in master_record.", customerId);
            return 1;
        }
        if (masters.Count > 1)
        {
            Log.Error("[documents] Customer '{CustomerId}' has multiple master records; document ownership is ambiguous.", customerId);
            return 1;
        }
        var master = masters[0];

        IReadOnlySet<string> references;
        if (string.Equals(master.ClientType, "L", StringComparison.OrdinalIgnoreCase))
        {
            var record = (await ctx.LegalEntities.GetByCustomerIdsAsync([customerId], ct)).SingleOrDefault();
            if (record is null) { Log.Error("[documents] No stored legal-entity record exists for '{CustomerId}'.", customerId); return 1; }
            references = DocumentReferences.For(record);
        }
        else
        {
            var record = (await ctx.Individuals.GetByCustomerIdsAsync([customerId], ct)).SingleOrDefault();
            if (record is null) { Log.Error("[documents] No stored individual record exists for '{CustomerId}'.", customerId); return 1; }
            references = DocumentReferences.For(record);
        }

        var staged = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToList();
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in staged)
        {
            var name = Path.GetFileName(path);
            if (!byName.TryAdd(name, path))
            {
                Log.Error("[documents] Duplicate case-insensitive staging filename: {FileName}", name);
                return 1;
            }
        }

        var missing = references.Where(name => !byName.ContainsKey(name)).OrderBy(x => x).ToList();
        var extras = byName.Keys.Where(name => !references.Contains(name)).OrderBy(x => x).ToList();
        foreach (var extra in extras) Log.Warn("[documents] Ignoring unreferenced file: {FileName}", extra);

        var imported = 0;
        foreach (var reference in references.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!byName.TryGetValue(reference, out var path)) continue;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await ctx.Documents.ImportAsync(
                new DocumentImport(master.Id, reference, null, "LocalStaging", Path.GetFullPath(path)), stream, ct);
            imported++;
            Log.Info("[documents] Imported {FileName} ({Length} bytes, SHA-256 {Hash})", reference, document.ByteLength, document.Sha256);
        }

        foreach (var name in missing) Log.Error("[documents] Missing referenced file: {FileName}", name);
        Log.Info("[documents] Customer={CustomerId} imported={Imported} missing={Missing} ignored={Ignored}", customerId, imported, missing.Count, extras.Count);
        return missing.Count == 0 ? 0 : 1;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
