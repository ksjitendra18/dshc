using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;

namespace CKYC.Files;

/// <summary>Writes the vendor individual-format-search record-10/20 pipe layout.</summary>
public sealed class CkycSearchWriter : ISearchFileWriter
{
    private readonly SearchSettings _settings;

    public CkycSearchWriter(SearchSettings settings) => _settings = settings;

    public string BuildFileName(DateOnly businessDate, int sequence)
        => $"{_settings.UserId}_{_settings.FiCode}_{businessDate:ddMMyyyy}_{sequence:00000}.SRC";

    public string Write(IReadOnlyList<SearchRequest> records, DateOnly businessDate)
    {
        var lines = new List<string>(records.Count + 1);
        var header = new string?[10];
        header[0] = "10";
        header[1] = _settings.FiCode;
        header[2] = _settings.RegionCode;
        header[3] = records.Count.ToString();
        header[4] = _settings.VersionNumber;
        header[5] = businessDate.ToString("dd-MM-yyyy");
        lines.Add(string.Join('|', header));

        for (var i = 0; i < records.Count; i++) lines.Add(BuildDetail(records[i], i + 1));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildDetail(SearchRequest record, int lineNumber)
    {
        var fields = new string?[21];
        fields[0] = "20";
        fields[1] = lineNumber.ToString();
        fields[2] = Clean(record.ClientType);
        fields[3] = record.SearchOption.ToString();
        fields[4] = Clean(record.IdentityTypeAndNumber);
        fields[5] = Clean(record.FirstName);
        fields[6] = Clean(record.MiddleName);
        fields[7] = Clean(record.LastName);
        fields[8] = Clean(record.DateOfBirth);
        fields[9] = Clean(record.LegalEntityName);
        fields[10] = Clean(record.DateOfIncorporation);
        fields[11] = Clean(record.Gender);
        fields[12] = Clean(record.PhotoReferenceNumber);
        fields[13] = Clean(record.Relation);
        fields[14] = Clean(record.RelationFirstName);
        fields[15] = Clean(record.RelationMiddleName);
        fields[16] = Clean(record.RelationLastName);
        fields[17] = Clean(record.MobileNumber);
        fields[18] = Clean(record.VerifiableCredential);
        fields[19] = Clean(record.Constitution);
        return string.Join('|', fields);
    }

    private static string Clean(string? value)
    {
        value ??= "";
        if (value.Contains('|') || value.Contains('\r') || value.Contains('\n'))
            throw new InvalidDataException("Search fields cannot contain pipe or newline characters.");
        return value.Trim();
    }
}
