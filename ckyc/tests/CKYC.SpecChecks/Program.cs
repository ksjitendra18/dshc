using System.Text.Json;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Spec;
using CKYC.Crm;
using CKYC.Data;
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

var tooManyIndividuals = Enumerable.Repeat(Read(), CkycRecords.MaxIndividualBatchRecords + 1).ToList();
try
{
    _ = new CkycBatchGenerator(new BatchSettings(), new FileHasher())
        .GenerateAsync(tooManyIndividuals, new DateOnly(2026, 8, 25));
    throw new InvalidOperationException("Individual generator accepted more than 500 customers.");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("cannot contain more than 500", StringComparison.Ordinal))
{
}

Console.WriteLine("All individual create-format specification checks passed.");

var legalProvider = new DummyCrmLegalEntityProvider();
var legalConstitutions = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R" };
foreach (var constitution in legalConstitutions)
{
    var legal = legalProvider.GetLegalEntity($"LEGAL-{constitution}", constitution);
    var legalErrors = LegalEntityRecordValidator.Validate(legal);
    if (legalErrors.Count != 0)
        throw new InvalidOperationException($"Legal constitution {constitution} unexpectedly failed: " +
            string.Join("; ", legalErrors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}")));
}

var company = legalProvider.GetLegalEntity("LEGAL-COMPANY", LeConstitution.PrivateLimitedCompany);
var legalLines = new CkycLegalEntityUploadWriter(new BatchSettings()).Write([company], new DateOnly(2026, 8, 25))
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
var legalWidths = new Dictionary<string, int>
{
    ["10"] = 11, ["20"] = 24, ["30"] = 36, ["40"] = 30, ["50"] = 11, ["60"] = 11, ["70"] = 20,
};
foreach (var line in legalLines)
{
    var fields = line.Split('|');
    if (!legalWidths.TryGetValue(fields[0], out var width) || fields.Length != width)
        throw new InvalidOperationException($"Legal record {fields[0]} emitted {fields.Length} fields; expected {width}.");
}
var relatedLines = legalLines.Where(line => line.StartsWith("60|", StringComparison.Ordinal)).Select(line => line.Split('|')).ToList();
if (relatedLines.Count != 2 || relatedLines.Any(fields => fields[3] != "2" || fields[4] != "1"))
    throw new InvalidOperationException("Legal record 60 did not emit related-person and beneficial-owner counts.");

var unsafeDocument = legalProvider.GetLegalEntity("LEGAL-UNSAFE", LeConstitution.PrivateLimitedCompany);
unsafeDocument.PanDocument = "../Pan.pdf";
if (!LegalEntityRecordValidator.Validate(unsafeDocument).Any(e => e.FieldName == "PAN document"))
    throw new InvalidOperationException("Legal validation accepted a document path traversal.");

var tooManyLegal = Enumerable.Range(1, CkycRecords.MaxLegalEntityBatchRecords + 1)
    .Select(i => legalProvider.GetLegalEntity($"LEGAL-LIMIT-{i}", LeConstitution.PrivateLimitedCompany)).ToList();
try
{
    _ = new CkycLegalEntityBatchGenerator(new BatchSettings(), new FileHasher())
        .GenerateAsync(tooManyLegal, new DateOnly(2026, 8, 25));
    throw new InvalidOperationException("Legal generator accepted more than 10 customers.");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("cannot contain more than 10", StringComparison.Ordinal))
{
}

var sizeTestRoot = Path.Combine(Path.GetTempPath(), $"ckyc-size-check-{Guid.NewGuid():N}");
try
{
    var date = new DateOnly(2026, 8, 25);
    var settings = new BatchSettings { OutputRoot = sizeTestRoot, ClientType = "I" };
    var individualBatchKey = Path.GetFileNameWithoutExtension(CkycFileName.Build("I", settings.UserId, settings.FiCode, date, settings.SequenceStart, "UPL"));
    var individualDocs = Path.Combine(sizeTestRoot, individualBatchKey, "upload", "support_docs");
    Directory.CreateDirectory(individualDocs);
    var oversizedIndividual = Read(); oversizedIndividual.CustomerId = "IND-SIZE-FAIL"; oversizedIndividual.PhotoOfIndividual = "oversized-individual.jpg";
    using (var stream = File.Create(Path.Combine(individualDocs, oversizedIndividual.PhotoOfIndividual))) stream.SetLength(CkycRecords.MaxIndividualBytesPerCustomer + 1);
    var individualSizeBatch = await new CkycBatchGenerator(settings, new FileHasher()).GenerateAsync([Read(), oversizedIndividual], date);
    if (individualSizeBatch.RecordCount != 1 || individualSizeBatch.SkippedCount != 1)
        throw new InvalidOperationException("Individual 500 KB per-customer size limit was not enforced.");

    settings = new BatchSettings { OutputRoot = sizeTestRoot };
    var legalBatchKey = Path.GetFileNameWithoutExtension(CkycFileName.Build("L", settings.UserId, settings.FiCode, date, settings.SequenceStart, "UPL"));
    var legalDocs = Path.Combine(sizeTestRoot, legalBatchKey, "upload", "support_docs");
    Directory.CreateDirectory(legalDocs);
    var oversizedLegal = legalProvider.GetLegalEntity("LEGAL-SIZE-FAIL", LeConstitution.PrivateLimitedCompany);
    oversizedLegal.Proofs[0].CertificateOfIncorporation = "oversized-legal.pdf";
    using (var stream = File.Create(Path.Combine(legalDocs, oversizedLegal.Proofs[0].CertificateOfIncorporation!))) stream.SetLength(CkycRecords.MaxLegalEntityBytesPerCustomer + 1);
    var legalSizeBatch = await new CkycLegalEntityBatchGenerator(settings, new FileHasher())
        .GenerateAsync([company, oversizedLegal], date);
    if (legalSizeBatch.RecordCount != 1 || legalSizeBatch.SkippedCount != 1)
        throw new InvalidOperationException("Legal 25 MB per-customer size limit was not enforced.");
}
finally
{
    if (Directory.Exists(sizeTestRoot)) Directory.Delete(sizeTestRoot, recursive: true);
}

var databaseTestRoot = Path.Combine(Path.GetTempPath(), $"ckyc-db-check-{Guid.NewGuid():N}");
Directory.CreateDirectory(databaseTestRoot);
try
{
    var databaseSettings = new DatabaseSettings { ConnectionString = $"Data Source={Path.Combine(databaseTestRoot, "legal.db")}" };
    using var database = new SqliteDatabase(databaseSettings);
    await using (var legacyConnection = database.Create())
    await using (var legacyCommand = legacyConnection.CreateCommand())
    {
        legacyCommand.CommandText = """
            CREATE TABLE legal_entity_record_60 (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, MasterRecordId INTEGER, CustomerId VARCHAR(50),
                Record20LineNumber INTEGER, Relation VARCHAR(60), CkycNumber VARCHAR(14),
                ControllingInterest VARCHAR(50), PercentageOwnership VARCHAR(10),
                OtherRelationName VARCHAR(33), Din VARCHAR(8))
            """;
        await legacyCommand.ExecuteNonQueryAsync();
    }
    await database.InitializeSchemaAsync();
    var stored = legalProvider.GetLegalEntity("LEGAL-DB-ROUNDTRIP", LeConstitution.PrivateLimitedCompany);
    stored.MasterRecordId = 987654;
    var repository = new LegalEntityRepository(database);
    var save = await repository.SaveAsync(stored);
    if (!save.Success) throw new InvalidOperationException($"Legal DB save failed: {save.Error}");
    var loaded = (await repository.GetByCustomerIdsAsync([stored.CustomerId])).Single();
    if (loaded.EntityName != stored.EntityName || loaded.Proofs.Single().Cin != stored.Proofs.Single().Cin
        || loaded.RelatedParties.Count != stored.RelatedParties.Count || loaded.Other?.EmployeeCode != stored.Other?.EmployeeCode)
        throw new InvalidOperationException("Legal entity did not round-trip through all dedicated DB record tables.");
    await using var connection = database.Create();
    await using var countCommand = connection.CreateCommand();
    countCommand.CommandText = "SELECT NumberOfRelatedPersons, NumberOfBeneficialOwners FROM legal_entity_record_60 WHERE MasterRecordId=987654 LIMIT 1";
    await using var countReader = await countCommand.ExecuteReaderAsync();
    if (!await countReader.ReadAsync() || countReader.GetInt32(0) != 2 || countReader.GetInt32(1) != 1)
        throw new InvalidOperationException("Legal related-person counts were not persisted correctly.");
}
finally
{
    if (Directory.Exists(databaseTestRoot)) Directory.Delete(databaseTestRoot, recursive: true);
}

Console.WriteLine("All legal-entity create-format specification checks passed.");
