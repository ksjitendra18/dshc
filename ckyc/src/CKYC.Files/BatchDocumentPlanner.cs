using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;

namespace CKYC.Files;

internal sealed class BatchDocumentPlanner
{
    private readonly Dictionary<(long MasterId, string Name), CustomerDocument> _documents;
    private readonly Dictionary<(string CustomerId, string Name), string> _batchNames;

    private BatchDocumentPlanner(
        Dictionary<(long, string), CustomerDocument> documents,
        Dictionary<(string, string), string> batchNames)
    {
        _documents = documents;
        _batchNames = batchNames;
    }

    public static async Task<(BatchDocumentPlanner Planner, Dictionary<long, List<string>> Missing)> CreateAsync(
        IDocumentStore store,
        IEnumerable<(long MasterId, string CustomerId, IReadOnlySet<string> References)> records,
        CancellationToken ct)
    {
        var input = records.ToList();
        var stored = await store.GetByMasterRecordIdsAsync(input.Select(x => x.MasterId).Distinct().ToArray(), ct);
        var documents = stored.ToDictionary(x => (x.MasterRecordId, Canonical(x.OriginalFileName)));
        var missing = new Dictionary<long, List<string>>();
        var resolved = new List<(long MasterId, string CustomerId, string Reference, CustomerDocument Document)>();

        foreach (var record in input)
        foreach (var reference in record.References)
        {
            if (documents.TryGetValue((record.MasterId, Canonical(reference)), out var document))
                resolved.Add((record.MasterId, record.CustomerId, reference, document));
            else
                (missing.TryGetValue(record.MasterId, out var names) ? names : missing[record.MasterId] = new()).Add(reference);
        }

        var batchNames = AllocateNames(resolved);
        return (new BatchDocumentPlanner(documents, batchNames), missing);
    }

    public string? Map(string customerId, string? fileName) => string.IsNullOrWhiteSpace(fileName)
        ? fileName
        : _batchNames.GetValueOrDefault((customerId, Canonical(fileName)), fileName);

    public CustomerDocument Get(long masterId, string reference) => _documents[(masterId, Canonical(reference))];

    public async Task MaterializeAsync(
        IEnumerable<(long MasterId, string CustomerId, IReadOnlySet<string> References)> records,
        string directory,
        CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        foreach (var reference in record.References)
        {
            var document = Get(record.MasterId, reference);
            var batchName = Map(record.CustomerId, reference)!;
            if (written.TryGetValue(batchName, out var hash))
            {
                if (!string.Equals(hash, document.Sha256, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Batch document name '{batchName}' maps to different content.");
                continue;
            }
            await File.WriteAllBytesAsync(Path.Combine(directory, batchName), document.Content, ct);
            written.Add(batchName, document.Sha256);
        }
    }

    private static Dictionary<(string, string), string> AllocateNames(
        IReadOnlyList<(long MasterId, string CustomerId, string Reference, CustomerDocument Document)> resolved)
    {
        var result = new Dictionary<(string, string), string>();
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in resolved.GroupBy(x => Canonical(x.Reference)).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var differentContent = group.Select(x => x.Document.Sha256).Distinct(StringComparer.Ordinal).Skip(1).Any();
            var sharedName = group.Select(x => x.Reference).OrderBy(x => x, StringComparer.Ordinal).First();
            foreach (var item in group.OrderBy(x => x.CustomerId, StringComparer.Ordinal).ThenBy(x => x.Document.Sha256, StringComparer.Ordinal))
            {
                var candidate = differentContent
                    ? PrefixedName(item.CustomerId, item.Reference, item.Document.Sha256)
                    : sharedName;
                if (used.TryGetValue(candidate, out var existingHash) && !string.Equals(existingHash, item.Document.Sha256, StringComparison.Ordinal))
                    candidate = PrefixedName(item.CustomerId, item.Reference, item.Document.Sha256);
                used[candidate] = item.Document.Sha256;
                result[(item.CustomerId, Canonical(item.Reference))] = candidate;
            }
        }
        return result;
    }

    private static string PrefixedName(string customerId, string original, string hash)
    {
        const int max = 125;
        var extension = Path.GetExtension(original);
        var stem = Path.GetFileNameWithoutExtension(original);
        var prefix = Sanitize(customerId) + "_";
        var suffix = "_" + hash[..8];
        var available = Math.Max(1, max - prefix.Length - suffix.Length - extension.Length);
        if (stem.Length > available) stem = stem[..available];
        return prefix + stem + suffix + extension.ToLowerInvariant();
    }

    private static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormKC))
            result.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        var safe = result.ToString().Trim('_');
        return string.IsNullOrEmpty(safe) ? "customer" : safe[..Math.Min(safe.Length, 50)];
    }

    private static string Canonical(string value) => value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
}
