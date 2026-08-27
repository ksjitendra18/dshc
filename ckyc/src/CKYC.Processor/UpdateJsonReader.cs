using System.Globalization;
using System.Text.Json;
using CKYC.Core.Domain;
using CKYC.Core.Spec;

namespace CKYC.Processor;

/// <summary>
/// Parses bulk-update intake JSON (<c>update-load</c>) into <see cref="UpdateRequest"/> rows.
///
/// Accepted shapes: an object, an array, or an object holding one of the wrapper arrays
/// <c>records</c> / <c>updates</c> / <c>requests</c> / <c>data</c>. Each submission carries
/// <c>clientType</c>, <c>ckycNumber</c> (the existing record being amended) plus any number of
/// format fields whose names must resolve to a key declared in
/// <see cref="UpdateFormat.DetailLayouts"/> — names are matched after stripping punctuation,
/// so <c>permPinCode</c>, <c>Perm_Pin_Code</c> and <c>perm pin code</c> are equivalent.
/// Calendar fields are normalised to DD-MM-YYYY (DDMMYYYY inside legal-entity dates), values
/// are length-checked against the sheet sizes, and at least one section update flag must be
/// "Y" for a submission to be meaningful.
/// </summary>
internal static class UpdateJsonReader
{
    private static readonly string[] NewLines = ["\r\n", "\n"];

    /// <summary>Envelope/metadata keys that never map to file fields.</summary>
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.Ordinal)
    {
        "note", "notes", "comment", "comments",
    };

    public static async Task<IReadOnlyList<UpdateRequest>> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var elements = GetRecords(document.RootElement).ToArray();
        if (elements.Length == 0) throw new InvalidDataException("No update records were found in the input.");

        var errors = new List<string>();
        var requests = new List<UpdateRequest>(elements.Length);
        for (var row = 0; row < elements.Length; row++)
        {
            var element = elements[row];
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Record {row + 1}: every update record must be a JSON object.");
            ParseRecord(element, row + 1, errors, requests);
        }

        if (errors.Count > 0)
            throw new InvalidDataException($"{errors.Count} problem(s) found in '{Path.GetFileName(path)}':{NewLines[0]}" +
                                           string.Join(NewLines[0], errors.Select(e => $"  - {e}")));
        return requests;
    }

    /// <summary>
    /// Rebuilds <see cref="UpdateRequest.Values"/> from the stored <see cref="UpdateRequest.RawRequestJson"/>.
    /// Intake rows carry only their JSON body through the claim round-trip; processing re-parses it
    /// verbatim so the submitted amendment is written exactly as loaded.
    /// </summary>
    internal static void HydrateValues(UpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RawRequestJson)) return;
        using var document = JsonDocument.Parse(request.RawRequestJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return;

        var properties = document.RootElement.EnumerateObject()
            .GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (normalizedName, value) in properties)
        {
            if (IsEnvelope(normalizedName)) continue;
            if (!UpdateFieldIndex.TryResolve(normalizedName, request.ClientType, out var field)) 
                throw new InvalidDataException($"Stored update for CKYC {request.CkycNumber} contains unknown field '{normalizedName}'.");
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            var stringValue = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue)) continue;
            stringValue = stringValue.Trim();
            if (field.Date) stringValue = NormalizeDate(stringValue) ?? stringValue;
            if (field.CompactDate) stringValue = NormalizeCompactDate(stringValue) ?? stringValue;
            request.Values[field.Key] = stringValue;
        }
    }

    private static void ParseRecord(JsonElement element, int row, List<string> errors, List<UpdateRequest> requests)
    {
        // Snapshot properties once: normalised-name -> raw JSON.
        var properties = element.EnumerateObject()
            .GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        static string? Text(IReadOnlyDictionary<string, JsonElement> source, string[] names)
        {
            foreach (var name in names.Select(Normalize))
            {
                if (!source.TryGetValue(name, out var value)) continue;
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
            return null;
        }

        var request = new UpdateRequest
        {
            ExternalRequestId = Text(properties, ["externalrequestid", "requestid", "id"]),
            CustomerId = Text(properties, ["customerid", "sourcecustomerid"]),
            ClientType = (Text(properties, ["clienttype"]) ?? "I").Trim().ToUpperInvariant(),
            CkycNumber = (Text(properties, ["ckycnumber"]) ?? string.Empty).Replace(" ", ""),
        };

        if (request.ClientType is not ("I" or "L"))
        {
            errors.Add($"Row {row}: ClientType must be 'I' (individual) or 'L' (legal entity), got '{request.ClientType}'.");
            return;
        }
        if (request.CkycNumber.Length == 0 || !request.CkycNumber.All(char.IsDigit) || request.CkycNumber.Length > 14)
        {
            errors.Add($"Row {row}: ckycNumber is mandatory and must be up to 14 digits (the existing record being updated).");
            return;
        }
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            request.CustomerId = $"CKYC-{request.CkycNumber}";

        // Resolve every remaining property onto its catalog field key.
        var anyFlagSet = false;
        foreach (var (normalizedName, value) in properties)
        {
            if (IsEnvelope(normalizedName)) continue;
            if (!UpdateFieldIndex.TryResolve(normalizedName, request.ClientType, out var field))
            {
                if (IgnoredNames.Contains(normalizedName)) continue;
                errors.Add($"Row {row}: '{normalizedName}' does not match any " +
                           (request.ClientType == "I" ? "individual" : "legal-entity") +
                           " update-format field. See vendor/individual-format-update.xlsx / legal-format-update.xlsx.");
                continue;
            }
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;

            var stringValue = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue)) continue;
            stringValue = stringValue.Trim();

            if (stringValue.Contains('|') || stringValue.Contains('\r') || stringValue.Contains('\n'))
            {
                errors.Add($"Row {row}: field '{field.Key}' cannot contain pipe or newline characters.");
                continue;
            }
            if (field.Date && NormalizeDate(stringValue) is { } dated) stringValue = dated;
            if (field.CompactDate && NormalizeCompactDate(stringValue) is { } compacted) stringValue = compacted;

            if (field.Size > 0 && stringValue.Length > field.Size)
            {
                errors.Add($"Row {row}: field '{field.Key}' exceeds its size {field.Size} ('{field.Title}').");
                continue;
            }

            if (field.Flag && string.Equals(stringValue, "Y", StringComparison.OrdinalIgnoreCase)) anyFlagSet = true;
            request.Values[field.Key] = stringValue;
        }

        if (!anyFlagSet)
        {
            errors.Add($"Row {row}: at least one section update flag (*Flg = \"Y\") is required; " +
                       "a .UPD submission amends specific sections rather than the whole record.");
            return;
        }

        request.RawRequestJson = element.GetRawText();
        requests.Add(request);
    }

    /// <summary>Whether a property belongs to the request envelope rather than the format.</summary>
    private static bool IsEnvelope(string normalizedName)
        => normalizedName is "clienttype" or "ckycnumber"
            or "externalrequestid" or "requestid" or "id" or "customerid" or "sourcecustomerid";

    private static IEnumerable<JsonElement> GetRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) yield return item;
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The update input must contain an object or array.");

        var properties = root.EnumerateObject().ToDictionary(
            property => Normalize(property.Name), property => property.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "records", "updates", "requests", "data" })
        {
            if (!properties.TryGetValue(name, out var array) || array.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in array.EnumerateArray()) yield return item;
            yield break;
        }
        yield return root;
    }

    /// <summary>Parses many human-friendly calendar spellings and re-emits DD-MM-YYYY.</summary>
    internal static string? NormalizeDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var formats = new[] { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "ddMMyyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed.ToString("dd-MM-yyyy") : value;
    }

    /// <summary>The legal-entity workbook stores incorporation dates compactly as DDMMYYYY.</summary>
    internal static string? NormalizeCompactDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var formats = new[] { "ddMMyyyy", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed.ToString("ddMMyyyy") : value;
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Lazily-built index of every catalogue field, for intake-time name resolution.</summary>
    private static class UpdateFieldIndex
    {
        private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<(string ClientType, UpdateFormat.Field Field)>>> Index =
            new(BuildIndex);

        /// <summary>
        /// Resolves a normalised JSON property name to the catalog field with that key.
        /// Returns false when the key exists only for the other client type, letting the caller
        /// report an explicit client-type mismatch instead of silently accepting a wrong field.
        /// </summary>
        public static bool TryResolve(string normalizedName, string clientType, out UpdateFormat.Field field)
        {
            field = null!;
            if (!Index.Value.TryGetValue(normalizedName, out var candidates)) return false;
            var match = candidates.FirstOrDefault(c => c.ClientType.Equals(clientType, StringComparison.OrdinalIgnoreCase));
            if (match.Field is not null) { field = match.Field; return true; }
            return false;
        }

        private static Dictionary<string, IReadOnlyList<(string, UpdateFormat.Field)>> BuildIndex()
        {
            var index = new Dictionary<string, IReadOnlyList<(string, UpdateFormat.Field)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var layout in UpdateFormat.DetailLayouts.SelectMany(g => g))
            {
                foreach (var field in layout.Fields)
                {
                    var key = Normalize(field.Key);
                    if (!index.TryGetValue(key, out var bucket))
                        index[key] = bucket = new List<(string, UpdateFormat.Field)>();
                    ((List<(string, UpdateFormat.Field)>)bucket).Add((layout.ClientType, field));
                }
            }
            return index;
        }
    }
}
