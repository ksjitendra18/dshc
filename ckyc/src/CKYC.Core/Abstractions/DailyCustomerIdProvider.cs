using System.Globalization;
using System.Text.Json;
using CKYC.Core.Configuration;

namespace CKYC.Core.Abstractions;

/// <summary>Produces the daily set of incoming customer ids for a business date.</summary>
public interface IDailyCustomerIdProvider
{
    IReadOnlyList<string> GetIds(DateOnly businessDate);
}

/// <summary>
/// Default provider. In "generate" mode it deterministically produces
/// <c>CUST&lt;yyyyMMdd&gt;&lt;seq&gt;</c> ids from a seed so that the source fetch and the
/// dummy CRM agree on the same set every run. In "file" mode it reads one id per line.
/// </summary>
public sealed class DailyCustomerIdProvider : IDailyCustomerIdProvider
{
    private readonly SourceSettings _settings;

    public DailyCustomerIdProvider(SourceSettings settings) => _settings = settings;

    public IReadOnlyList<string> GetIds(DateOnly businessDate)
    {
        if (string.Equals(_settings.Mode, "file", StringComparison.OrdinalIgnoreCase) && _settings.FilePath is not null)
            return ReadCustomerIdsFile(_settings.FilePath);

        var count = Math.Max(0, _settings.GenerateCount);
        var ids = new string[count];
        for (var i = 0; i < count; i++)
        {
            // Deterministic but "daily" — the seed keeps the same set within a day and
            // distinct ids across days.
            var seq = (i + 1).ToString("D4", CultureInfo.InvariantCulture);
            ids[i] = $"CUST{businessDate:yyyyMMdd}{seq}";
        }
        return ids;
    }

    /// <summary>
    /// Reads a customer-id source file. A <c>.json</c> file is parsed as a JSON array of ids
    /// or as an object with a <c>customerId</c> / <c>customerIds</c> property; any other file
    /// is treated as plain text with one customer id per line.
    /// </summary>
    public static IReadOnlyList<string> ReadCustomerIdsFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Customer id file not found.", filePath);

        if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            return ReadCustomerIdsJson(filePath);

        return File.ReadAllLines(filePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToArray();
    }

    private static List<string> ReadCustomerIdsJson(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        using var doc = JsonDocument.Parse(text, options);
        var root = doc.RootElement;
        var list = new List<string>();

        static void AddString(string? value, List<string> list)
        {
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value!.Trim());
        }

        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in root.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) AddString(item.GetString(), list);
                break;

            case JsonValueKind.Object:
                foreach (var propName in new[] { "customerIds", "custIds", "ids", "customerId", "id" })
                {
                    if (!root.TryGetProperty(propName, out var prop)) continue;
                    if (prop.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.String) AddString(item.GetString(), list);
                    }
                    else
                    {
                        AddString(prop.GetString(), list);
                    }
                    break;
                }
                break;
        }

        return list;
    }
}
