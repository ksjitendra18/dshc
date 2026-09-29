using System.Globalization;
using System.Text.Json;
using CKYC.Core.Configuration;

namespace CKYC.Core.Abstractions;

/// <summary>
/// One customer id produced by the daily source fetch, together with the optional document
/// key (<c>dockey</c>) that locates the customer's image/document on the channel's source
/// (e.g. the beckyc SFTP folder) and the optional intake channel it originated from.
/// </summary>
public sealed record SourceCustomer(string CustomerId, string? DocumentKey = null, string? Source = null);

/// <summary>Produces the daily set of incoming customer ids for a business date.</summary>
public interface IDailyCustomerIdProvider
{
    IReadOnlyList<string> GetIds(DateOnly businessDate);

    /// <summary>
    /// The daily source records: each customer id with its document key and intake channel.
    /// Used by <c>fetch</c> so the image/document fetch step knows what to pull.
    /// </summary>
    IReadOnlyList<SourceCustomer> GetCustomers(DateOnly businessDate);
}

/// <summary>
/// Default provider. In "generate" mode it deterministically produces
/// <c>CUST&lt;yyyyMMdd&gt;&lt;seq&gt;</c> ids from a seed so that the source fetch and the
/// dummy CRM agree on the same set every run. In "file" mode it reads the source file.
/// </summary>
public sealed class DailyCustomerIdProvider : IDailyCustomerIdProvider
{
    private readonly SourceSettings _settings;

    public DailyCustomerIdProvider(SourceSettings settings) => _settings = settings;

    public IReadOnlyList<string> GetIds(DateOnly businessDate)
        => GetCustomers(businessDate).Select(c => c.CustomerId).ToList();

    public IReadOnlyList<SourceCustomer> GetCustomers(DateOnly businessDate)
    {
        if (string.Equals(_settings.Mode, "file", StringComparison.OrdinalIgnoreCase) && _settings.FilePath is not null)
            return ReadCustomersFile(_settings.FilePath, _settings.DocumentKeyProperty);

        var count = Math.Max(0, _settings.GenerateCount);
        var customers = new List<SourceCustomer>(count);
        for (var i = 0; i < count; i++)
        {
            // Deterministic but "daily" — the seed keeps the same set within a day and
            // distinct ids across days. Generate mode has no document key; the fetch step
            // falls back to the customer id when the key is absent.
            var seq = (i + 1).ToString("D4", CultureInfo.InvariantCulture);
            customers.Add(new SourceCustomer($"CUST{businessDate:yyyyMMdd}{seq}"));
        }
        return customers;
    }

    /// <summary>
    /// Reads a customer-id source file. A <c>.json</c> file is parsed as a JSON array (of ids or
    /// <c>{ "customerId", "&lt;keyField&gt;" }</c> objects) or as an object with
    /// <c>customerId</c>/<c>customerIds</c> plus the document-key field; any other file is
    /// treated as plain text with one customer per line. Each line is
    /// <c>customerId&lt;sep&gt;documentKey&lt;sep&gt;source</c> (comma, tab, pipe or semicolon).
    /// </summary>
    /// <param name="documentKeyProperty">
    /// Name of the field carrying the dockey, when the source names it something other than the
    /// accepted aliases (<c>documentKey</c>, <c>dockey</c>, <c>docKey</c>, …).
    /// </param>
    public static IReadOnlyList<string> ReadCustomerIdsFile(string filePath, string? documentKeyProperty = null)
        => ReadCustomersFile(filePath, documentKeyProperty).Select(c => c.CustomerId).ToList();

    /// <summary>Reads the daily source records (customer id + document key + channel).</summary>
    public static IReadOnlyList<SourceCustomer> ReadCustomersFile(string filePath, string? documentKeyProperty = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Customer id file not found.", filePath);

        if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            return ReadCustomersJson(filePath, documentKeyProperty);

        var customers = new List<SourceCustomer>();
        foreach (var raw in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Split([',', '\t', '|', ';'], StringSplitOptions.TrimEntries);
            var id = parts.Length > 0 ? parts[0] : string.Empty;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var key = parts.Length > 1 ? parts[1] : null;
            var source = parts.Length > 2 ? parts[2] : null;
            customers.Add(new SourceCustomer(id, string.IsNullOrWhiteSpace(key) ? null : key,
                string.IsNullOrWhiteSpace(source) ? null : source));
        }
        return customers;
    }

    private static List<SourceCustomer> ReadCustomersJson(string filePath, string? documentKeyProperty)
    {
        var text = File.ReadAllText(filePath);
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        using var doc = JsonDocument.Parse(text, options);
        var root = doc.RootElement;
        var list = new List<SourceCustomer>();
        var keyNames = KeyNames(documentKeyProperty);

        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in root.EnumerateArray())
                    AddElement(item, list, null, null, keyNames);
                break;

            case JsonValueKind.Object:
                // A single record with top-level document key/source, or a wrapper array.
                var topKey = ReadString(root, keyNames);
                var topSource = ReadString(root, "source", "channel");
                var handled = false;
                foreach (var propName in new[] { "customers", "customerIds", "custIds", "ids", "records", "data" })
                {
                    if (!root.TryGetProperty(propName, out var prop) || prop.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in prop.EnumerateArray())
                        AddElement(item, list, topKey, topSource, keyNames);
                    handled = true;
                    break;
                }
                if (!handled)
                {
                    foreach (var propName in new[] { "customerId", "custId", "id" })
                    {
                        var id = ReadString(root, propName);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        list.Add(new SourceCustomer(id, topKey, topSource));
                        handled = true;
                        break;
                    }
                }
                break;
        }

        return list;
    }

    /// <summary>The configured document-key property name first, then the accepted aliases.</summary>
    private static string[] KeyNames(string? configured)
    {
        var aliases = new[] { "documentKey", "dockey", "docKey", "document_key", "documentId", "docId", "imageKey" };
        if (string.IsNullOrWhiteSpace(configured)) return aliases;
        return aliases.Prepend(configured.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddElement(JsonElement item, List<SourceCustomer> list, string? fallbackKey, string? fallbackSource, string[] keyNames)
    {
        switch (item.ValueKind)
        {
            case JsonValueKind.String:
                Add(item.GetString(), fallbackKey, fallbackSource, list);
                break;

            case JsonValueKind.Object:
                var id = ReadString(item, "customerId", "custId", "id");
                var key = ReadString(item, keyNames) ?? fallbackKey;
                var source = ReadString(item, "source", "channel") ?? fallbackSource;
                Add(id, key, source, list);
                break;
        }
    }

    private static void Add(string? id, string? key, string? source, List<SourceCustomer> list)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        list.Add(new SourceCustomer(id.Trim(),
            string.IsNullOrWhiteSpace(key) ? null : key.Trim(),
            string.IsNullOrWhiteSpace(source) ? null : source.Trim()));
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }
}
