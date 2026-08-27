using System.Globalization;
using CKYC.Core.Domain;

namespace CKYC.Core.Spec;

/// <summary>Validates record-20 search requests against individual-format-search.xlsx.</summary>
public static class SearchRequestValidator
{
    public static void ValidateAndNormalize(SearchRequest request, int row)
    {
        request.ClientType = request.ClientType.Trim().ToUpperInvariant();
        if (request.ClientType is not ("I" or "L"))
            throw new InvalidDataException($"Record {row}: ClientType must be I or L.");

        var maximumOption = request.ClientType == "I" ? 5 : 3;
        if (request.SearchOption < 1 || request.SearchOption > maximumOption)
            throw new InvalidDataException(
                $"Record {row}: SearchOption must be between 1 and {maximumOption} for ClientType {request.ClientType}.");

        Maximum(request.IdentityTypeAndNumber, 2000, "IdentityTypeAndNumber", row);
        Maximum(request.FirstName, 33, "FirstName", row);
        Maximum(request.MiddleName, 33, "MiddleName", row);
        Maximum(request.LastName, 33, "LastName", row);
        Maximum(request.LegalEntityName, 99, "LegalEntityName", row);
        Maximum(request.PhotoReferenceNumber, 40, "PhotoReferenceNumber", row);
        Maximum(request.Relation, 50, "Relation", row);
        Maximum(request.RelationFirstName, 33, "RelationFirstName", row);
        Maximum(request.RelationMiddleName, 33, "RelationMiddleName", row);
        Maximum(request.RelationLastName, 33, "RelationLastName", row);
        Maximum(request.VerifiableCredential, 50, "VerifiableCredential", row);

        var identityTypes = request.SearchOption == 1
            ? IdentityTypes(request.IdentityTypeAndNumber, request.ClientType, row)
            : new HashSet<string>(StringComparer.Ordinal);

        if (request.ClientType == "I")
        {
            var aadhaarSearch = identityTypes.Contains("E");
            if (request.SearchOption is 2 or 3 || aadhaarSearch)
                Required(request.FirstName, "FirstName", row);
            if (request.SearchOption == 2 || aadhaarSearch)
            {
                ValidDate(request.DateOfBirth, "DateOfBirth", row);
                RequiredAllowed(request.Gender, "Gender", ["M", "F", "T"], row);
            }
            if (request.SearchOption == 2)
            {
                Required(request.Relation, "Relation", row);
                Required(request.RelationFirstName, "RelationFirstName", row);
            }
            if (request.SearchOption == 3) Required(request.PhotoReferenceNumber, "PhotoReferenceNumber", row);
            if (request.SearchOption == 4) ValidMobile(request.MobileNumber, row);
            if (request.SearchOption == 5) Required(request.VerifiableCredential, "VerifiableCredential", row);
        }
        else if (request.SearchOption == 2)
        {
            Required(request.LegalEntityName, "LegalEntityName", row);
            ValidDate(request.DateOfIncorporation, "DateOfIncorporation", row);
            RequiredAllowed(request.Constitution, "Constitution",
                Enumerable.Range('A', 'R' - 'A' + 1).Select(value => ((char)value).ToString()).ToArray(), row);
        }
        else if (request.SearchOption == 3)
        {
            ValidMobile(request.MobileNumber, row);
        }
    }

    private static HashSet<string> IdentityTypes(string? value, string clientType, int row)
    {
        Required(value, "IdentityTypeAndNumber", row);
        var parts = value!.Split('^');
        if (parts.Length == 0 || parts.Length % 2 != 0)
            throw new InvalidDataException(
                $"Record {row}: IdentityTypeAndNumber must contain type/number pairs separated by '^'.");

        var allowed = clientType == "I"
            ? new HashSet<string>(["A", "B", "C", "D", "E", "F", "G", "Z"], StringComparer.Ordinal)
            : new HashSet<string>(["C", "H", "I", "J", "Z"], StringComparer.Ordinal);
        var types = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < parts.Length; index += 2)
        {
            var type = parts[index].Trim().ToUpperInvariant();
            var number = parts[index + 1].Trim();
            if (!allowed.Contains(type))
                throw new InvalidDataException($"Record {row}: identity type '{type}' is not valid for ClientType {clientType}.");
            if (number.Length is < 1 or > 40 || !number.All(char.IsLetterOrDigit))
                throw new InvalidDataException($"Record {row}: identity number for type {type} must be 1-40 alphanumeric characters.");
            if (type == "E" && (number.Length != 4 || !number.All(char.IsDigit)))
                throw new InvalidDataException($"Record {row}: Aadhaar search must use exactly the last four digits.");
            types.Add(type);
        }
        if (types.Contains("Z") && types.Count > 1)
            throw new InvalidDataException($"Record {row}: CKYC number identity type Z cannot be combined with another identity type.");
        return types;
    }

    private static void Maximum(string? value, int maximum, string field, int row)
    {
        if (value?.Length > maximum)
            throw new InvalidDataException($"Record {row}: {field} cannot exceed {maximum} characters.");
    }

    private static void Required(string? value, string field, int row)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Record {row}: {field} is mandatory for the selected search option.");
    }

    private static void RequiredAllowed(string? value, string field, IReadOnlyCollection<string> allowed, int row)
    {
        Required(value, field, row);
        if (!allowed.Contains(value!.Trim().ToUpperInvariant()))
            throw new InvalidDataException($"Record {row}: {field} must be one of {string.Join(", ", allowed)}.");
    }

    private static void ValidDate(string? value, string field, int row)
    {
        Required(value, field, row);
        if (!DateTime.TryParseExact(value, "dd-MM-yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new InvalidDataException($"Record {row}: {field} must be a valid DD-MM-YYYY date.");
    }

    private static void ValidMobile(string? value, int row)
    {
        Required(value, "MobileNumber", row);
        if (value!.Length != 10 || !value.All(char.IsDigit))
            throw new InvalidDataException($"Record {row}: MobileNumber must contain exactly 10 digits.");
    }
}
