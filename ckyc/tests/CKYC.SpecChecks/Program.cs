using System.Text.Json;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Spec;
using CKYC.Files;

if (args.Length != 1)
    throw new ArgumentException("Pass the retail-customer.json path.");

var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var sourceJson = File.ReadAllText(args[0]);

Individual Read() => JsonSerializer.Deserialize<Individual>(sourceJson, options)
    ?? throw new InvalidDataException("Could not read the retail sample.");

void AssertValid(Individual record, string scenario)
{
    var errors = CkycRecordValidator.Validate(record);
    if (errors.Count != 0)
        throw new InvalidOperationException($"{scenario} unexpectedly failed: " +
            string.Join("; ", errors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}")));
}

void AssertError(Individual record, string fieldName, string scenario)
{
    var errors = CkycRecordValidator.Validate(record);
    if (!errors.Any(e => string.Equals(e.FieldName, fieldName, StringComparison.Ordinal)))
        throw new InvalidOperationException($"{scenario} did not report the expected '{fieldName}' failure.");
}

AssertValid(Read(), "retail sample");

var writer = new CkycUploadWriter(new BatchSettings());
var lines = writer.Write([Read()], new DateOnly(2026, 8, 25))
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
var expectedWidths = new Dictionary<string, int>
{
    ["10"] = 11, ["20"] = 56, ["30"] = 22, ["40"] = 46, ["50"] = 10, ["70"] = 23,
};
foreach (var line in lines)
{
    var fields = line.Split('|');
    if (!expectedWidths.TryGetValue(fields[0], out var width) || fields.Length != width)
        throw new InvalidOperationException($"Record {fields[0]} emitted {fields.Length} fields; expected {width}.");
}
var addressFields = lines.Single(line => line.StartsWith("40|", StringComparison.Ordinal)).Split('|');
if (addressFields[15] != "Y" || addressFields[37] != "Y" || addressFields[39] != "Y"
    || addressFields[40] != "Y" || addressFields[41] != "Y")
    throw new InvalidOperationException("Same-as-permanent record 40 did not emit its mandatory flags.");

var noContact = Read();
noContact.Contact = null;
AssertValid(noContact, "optional contact omitted");
var noContactLines = writer.Write([noContact], new DateOnly(2026, 8, 25))
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
if (noContactLines.Any(line => line.StartsWith("50|", StringComparison.Ordinal)))
    throw new InvalidOperationException("An omitted optional contact unexpectedly emitted record 50.");

var noPanAttachment = Read();
noPanAttachment.PanDocument = null;
AssertValid(noPanAttachment, "optional PAN attachment omitted");

var missingGenderMatch = Read();
missingGenderMatch.GenderMatchWithOvd = null;
AssertError(missingGenderMatch, "Gender matching with OVD", "gender CM rule");

var offlineAadhaar = Read();
offlineAadhaar.Proofs[0].ModeOfAadhaarVerification = "C";
offlineAadhaar.Proofs[0].DataFromOfflineVerification = null;
AssertError(offlineAadhaar, "Data received from offline verification", "offline Aadhaar CM rule");

var youngMinor = Read();
youngMinor.DateOfBirth = DateOnly.FromDateTime(DateTime.Today).AddYears(-5).ToString("dd-MM-yyyy");
youngMinor.KycType = "M";
youngMinor.Minor = "Y";
youngMinor.RelatedParties.Clear();
AssertError(youngMinor, "Related Party Details", "guardian-below-ten CM rule");

Console.WriteLine("All individual create-format specification checks passed.");
