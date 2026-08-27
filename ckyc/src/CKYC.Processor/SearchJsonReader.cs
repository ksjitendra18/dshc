using System.Globalization;
using System.Text.Json;
using CKYC.Core.Domain;
using CKYC.Core.Spec;

namespace CKYC.Processor;

internal static class SearchJsonReader
{
    public static async Task<IReadOnlyList<SearchRequest>> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var elements = GetRecords(document.RootElement).ToArray();
        var result = new List<SearchRequest>(elements.Length);
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Each search_customer.json record must be a JSON object.");
            var values = element.EnumerateObject().ToDictionary(
                p => Normalize(p.Name), p => p.Value, StringComparer.OrdinalIgnoreCase);
            var request = new SearchRequest
            {
                ExternalRequestId = Get(values, "externalrequestid", "requestid", "id"),
                CustomerId = Get(values, "customerid", "custid", "sourcecustomerid"), // final alias is legacy input compatibility
                ClientType = Get(values, "clienttype") ?? "I",
                SearchOption = GetInt(values, "searchoption", "option"),
                IdentityTypeAndNumber = Get(values, "identitytypeandnumber", "identitytypenumber", "identity"),
                FirstName = Get(values, "firstname", "namefirst"),
                MiddleName = Get(values, "middlename", "namemiddle"),
                LastName = Get(values, "lastname", "namelast"),
                DateOfBirth = Date(Get(values, "dateofbirth", "dob")),
                LegalEntityName = Get(values, "legalentityname"),
                DateOfIncorporation = Date(Get(values, "dateofincorporation", "registrationdate", "doi")),
                Gender = Get(values, "gender"),
                PhotoReferenceNumber = Get(values, "photoreferencenumber", "photoreference", "photo"),
                Relation = Get(values, "relation", "relationtype"),
                RelationFirstName = Get(values, "relationfirstname"),
                RelationMiddleName = Get(values, "relationmiddlename"),
                RelationLastName = Get(values, "relationlastname"),
                MobileNumber = Get(values, "mobilenumber", "mobileno", "mobile"),
                VerifiableCredential = Get(values, "verifiablecredential", "credential"),
                Constitution = Get(values, "constitution"),
                RawRequestJson = element.GetRawText(),
            };
            Validate(request, result.Count + 1);
            result.Add(request);
        }
        return result;
    }

    private static IEnumerable<JsonElement> GetRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) yield return item;
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("search_customer.json must contain an object or array.");

        var properties = root.EnumerateObject().ToDictionary(
            property => Normalize(property.Name), property => property.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "records", "requests", "customers", "searchCustomers", "data" })
        {
            if (!properties.TryGetValue(Normalize(name), out var array) || array.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in array.EnumerateArray()) yield return item;
            yield break;
        }
        yield return root;
    }

    private static void Validate(SearchRequest request, int row)
        => SearchRequestValidator.ValidateAndNormalize(request, row);

    private static int GetInt(IReadOnlyDictionary<string, JsonElement> values, params string[] names)
        => int.TryParse(Get(values, names), out var result) ? result : 0;

    private static string? Get(IReadOnlyDictionary<string, JsonElement> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (!values.TryGetValue(Normalize(name), out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        return null;
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var formats = new[] { "dd-MM-yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed.ToString("dd-MM-yyyy") : value;
    }
}
