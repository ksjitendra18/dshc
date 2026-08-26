using System.Text.Json;
using System.IO.Compression;
using CKYC.Core.Abstractions;
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

static byte[] ValidDocumentBytes(string fileName, string marker = "fixture")
{
    if (Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        return System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n{marker}");
    return [0xff, 0xd8, 0xff, .. System.Text.Encoding.ASCII.GetBytes(marker), 0xff, 0xd9];
}

static async Task ImportDocumentsAsync(IDocumentStore store, long masterId, IEnumerable<string> names,
    Func<string, byte[]>? content = null)
{
    foreach (var name in names)
    {
        await using var stream = new MemoryStream(content is null ? ValidDocumentBytes(name) : content(name));
        await store.ImportAsync(new DocumentImport(masterId, name, null, "SpecCheck", name), stream);
    }
}

AssertValid(Read(), "retail sample");

var sameCurrentAddress = Read();
var permanent = sameCurrentAddress.PermanentAddress!;
sameCurrentAddress.CurrentAddress = new AddressDetails
{
    Line1 = permanent.Line1, Line2 = permanent.Line2, Line3 = permanent.Line3,
    Country = permanent.Country, State = permanent.State, District = permanent.District,
    City = permanent.City, PinCode = permanent.PinCode,
    AddressSupportedWithDocument = permanent.AddressSupportedWithDocument,
    AddressMatchWithOvd = permanent.AddressMatchWithOvd,
};
AssertValid(sameCurrentAddress, "legacy copied current address with same-as-permanent Y");

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
if (addressFields[15] != "Y" || addressFields[16] != "" || addressFields[37] != "N"
    || addressFields[39] != "Y" || addressFields[40] != "N" || addressFields[41] != "Y")
    throw new InvalidOperationException("Same-as-permanent record 40 did not emit its mandatory flags.");

var legacyAddressFields = writer.Write([sameCurrentAddress], new DateOnly(2026, 8, 25))
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
    .Single(line => line.StartsWith("40|", StringComparison.Ordinal)).Split('|');
if (legacyAddressFields.Skip(16).Take(21).Any(value => value != "")
    || legacyAddressFields[37] != "Y" || legacyAddressFields[39] != "Y"
    || legacyAddressFields[40] != "Y" || legacyAddressFields[41] != "Y")
    throw new InvalidOperationException("Legacy same-address data was not normalized in the emitted record 40.");

var differentCurrentAddress = Read();
differentCurrentAddress.CurrentAddressSameAsPermanent = "N";
AssertError(differentCurrentAddress, "Current Address Line 1", "different-current-address CM rule");

var missingRemoteGeoTagging = Read();
missingRemoteGeoTagging.CurrentAddressSameAsPermanent = "N";
missingRemoteGeoTagging.CurrentAddress!.RemoteGeoTagging = null;
AssertError(missingRemoteGeoTagging, "Remote Geo Tagging", "record-40 mandatory geo-tagging rule");

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
var demographicFields = writer.Write([noPanAttachment], new DateOnly(2026, 8, 25))
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
    .Single(line => line.StartsWith("20|", StringComparison.Ordinal)).Split('|');
if (demographicFields[48] != "" || demographicFields[35] != "Y")
    throw new InvalidOperationException("An omitted optional PAN attachment was emitted or PAN verified was not set.");

var noPanNumber = Read();
noPanNumber.Pan = null;
noPanNumber.PanVerified = null;
noPanNumber.Form97Provided = "Y";
AssertValid(noPanNumber, "Form 97 used when PAN is absent");

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
    _ = await new CkycBatchGenerator(new BatchSettings(), new FileHasher(), null!)
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
    _ = await new CkycLegalEntityBatchGenerator(new BatchSettings(), new FileHasher(), null!)
        .GenerateAsync(tooManyLegal, new DateOnly(2026, 8, 25));
    throw new InvalidOperationException("Legal generator accepted more than 10 customers.");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("cannot contain more than 10", StringComparison.Ordinal))
{
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

    var masterRepository = new MasterRepository(database);
    var documentStore = new SqliteDocumentStore(database);
    var addressRoundTrip = new DummyCrmDataProvider().GetCustomer("ADDRESS-ROUNDTRIP");
    var addressMaster = await masterRepository.EnsureAsync(addressRoundTrip.CustomerId, new DateOnly(2026, 8, 25));
    addressRoundTrip.MasterRecordId = addressMaster.Id;
    var addressSave = await new IndividualRepository(database).SaveAsync(addressRoundTrip);
    if (!addressSave.Success) throw new InvalidOperationException($"Address DB save failed: {addressSave.Error}");
    var loadedAddress = (await new IndividualRepository(database).GetByCustomerIdsAsync([addressRoundTrip.CustomerId])).Single();
    var expectedCurrent = addressRoundTrip.CurrentAddress!;
    var actualCurrent = loadedAddress.CurrentAddress!;
    if (loadedAddress.CurrentAddressSameAsPermanent != addressRoundTrip.CurrentAddressSameAsPermanent
        || actualCurrent.ProofOfAddress != expectedCurrent.ProofOfAddress
        || actualCurrent.ProofOfAddressType != expectedCurrent.ProofOfAddressType
        || actualCurrent.RemoteGeoTagging != expectedCurrent.RemoteGeoTagging
        || actualCurrent.PositiveVerification != expectedCurrent.PositiveVerification
        || actualCurrent.PhysicalVerificationByThirdParty != expectedCurrent.PhysicalVerificationByThirdParty
        || actualCurrent.PhysicalVerificationByReOfficial != expectedCurrent.PhysicalVerificationByReOfficial
        || actualCurrent.CopyOfOvd != expectedCurrent.CopyOfOvd)
        throw new InvalidOperationException("Current-address proof fields did not round-trip through record 40.");
    var addressErrors = CkycRecordValidator.Validate(loadedAddress).Where(error => error.RecordType == "40").ToList();
    if (addressErrors.Count != 0)
        throw new InvalidOperationException("A valid different current address failed record-40 validation after database reload: "
            + string.Join("; ", addressErrors.Select(error => $"{error.FieldName}: {error.ErrorDescription}")));

    var first = Read(); first.CustomerId = "DOC-CUSTOMER-1";
    var second = Read(); second.CustomerId = "DOC-CUSTOMER-2";
    var firstMaster = await masterRepository.EnsureAsync(first.CustomerId, new DateOnly(2026, 8, 25));
    var secondMaster = await masterRepository.EnsureAsync(second.CustomerId, new DateOnly(2026, 8, 25));
    first.MasterRecordId = firstMaster.Id; second.MasterRecordId = secondMaster.Id;

    await ImportDocumentsAsync(documentStore, first.MasterRecordId, DocumentReferences.For(first),
        name => ValidDocumentBytes(name, name.Equals(first.PhotoOfIndividual, StringComparison.OrdinalIgnoreCase) ? "photo-one" : name));
    await ImportDocumentsAsync(documentStore, second.MasterRecordId, DocumentReferences.For(second),
        name => ValidDocumentBytes(name, name.Equals(second.PhotoOfIndividual, StringComparison.OrdinalIgnoreCase) ? "photo-two" : name));

    var retrieved = await documentStore.GetAsync(first.MasterRecordId, first.PhotoOfIndividual!);
    if (retrieved is null || !retrieved.Content.SequenceEqual(ValidDocumentBytes(first.PhotoOfIndividual!, "photo-one")))
        throw new InvalidOperationException("Document content did not round-trip byte-for-byte.");

    await using (var dedupConnection = database.Create())
    await using (var dedupCommand = dedupConnection.CreateCommand())
    {
        dedupCommand.CommandText = "SELECT COUNT(*) FROM file_content";
        var contentRows = Convert.ToInt32(await dedupCommand.ExecuteScalarAsync());
        var distinctExpected = DocumentReferences.For(first).Count + 1; // every shared fixture plus the second photo
        if (contentRows != distinctExpected)
            throw new InvalidOperationException($"SHA-256 deduplication produced {contentRows} rows; expected {distinctExpected}.");
    }

    var batchOutput = Path.Combine(databaseTestRoot, "document-batches");
    var batchSettings = new BatchSettings { OutputRoot = batchOutput, ClientType = "I" };
    var documentBatch = await new CkycBatchGenerator(batchSettings, new FileHasher(), documentStore)
        .GenerateAsync([first, second], new DateOnly(2026, 8, 25));
    if (first.PhotoOfIndividual != "Photo.jpg" || second.PhotoOfIndividual != "Photo.jpg")
        throw new InvalidOperationException("Batch collision handling mutated persisted document filenames.");
    var emittedPhotoNames = File.ReadAllLines(documentBatch.UploadFilePath)
        .Where(line => line.StartsWith("20|", StringComparison.Ordinal)).Select(line => line.Split('|')[49]).ToList();
    if (emittedPhotoNames.Count != 2 || emittedPhotoNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2
        || emittedPhotoNames.Any(name => name.Length > 125))
        throw new InvalidOperationException("Different same-name photos were not renamed deterministically within CKYC limits.");
    using (var archive = ZipFile.OpenRead(documentBatch.ZipPath!))
        if (emittedPhotoNames.Any(name => archive.GetEntry($"upload/support_docs/{name}") is null))
            throw new InvalidOperationException("Renamed database documents were not included in the batch ZIP.");

    var missingRecord = Read(); missingRecord.CustomerId = "DOC-MISSING";
    var missingMaster = await masterRepository.EnsureAsync(missingRecord.CustomerId, new DateOnly(2026, 8, 25));
    missingRecord.MasterRecordId = missingMaster.Id;
    var partialBatch = await new CkycBatchGenerator(
        new BatchSettings { OutputRoot = Path.Combine(databaseTestRoot, "missing-batch"), ClientType = "I" }, new FileHasher(), documentStore)
        .GenerateAsync([first, missingRecord], new DateOnly(2026, 8, 25));
    if (partialBatch.RecordCount != 1 || partialBatch.SkippedCount != 1)
        throw new InvalidOperationException("A customer with missing database documents was not skipped.");

    var oldHash = retrieved.Sha256;
    await using (var replacement = new MemoryStream(ValidDocumentBytes(first.PhotoOfIndividual!, "replacement")))
        await documentStore.ImportAsync(new DocumentImport(first.MasterRecordId, first.PhotoOfIndividual!, "Photo", "SpecCheck", "replacement"), replacement);
    if ((await documentStore.GetAsync(first.MasterRecordId, first.PhotoOfIndividual!))?.Sha256 == oldHash)
        throw new InvalidOperationException("Reimporting a logical filename did not replace its content association.");

    try
    {
        await using var invalid = new MemoryStream("not a jpeg"u8.ToArray());
        await documentStore.ImportAsync(new DocumentImport(first.MasterRecordId, "invalid.jpg", null, "SpecCheck", null), invalid);
        throw new InvalidOperationException("Document store accepted a MIME/signature mismatch.");
    }
    catch (InvalidDataException) { }

    foreach (var invalidName in new[] { "../escape.pdf", "unsupported.png" })
    {
        try
        {
            await using var invalidNameContent = new MemoryStream(ValidDocumentBytes("valid.pdf"));
            await documentStore.ImportAsync(new DocumentImport(first.MasterRecordId, invalidName, null, "SpecCheck", null), invalidNameContent);
            throw new InvalidOperationException($"Document store accepted invalid filename '{invalidName}'.");
        }
        catch (InvalidDataException) { }
    }

    try
    {
        var oversized = new byte[CkycRecords.MaxIndividualBytesPerCustomer];
        oversized[0] = 0xff; oversized[1] = 0xd8; oversized[2] = 0xff;
        await using var oversizedStream = new MemoryStream(oversized);
        await documentStore.ImportAsync(new DocumentImport(first.MasterRecordId, "oversized.jpg", null, "SpecCheck", null), oversizedStream);
        throw new InvalidOperationException("Document store accepted a customer total above 500 KB.");
    }
    catch (InvalidDataException) { }

    try
    {
        await using var unknownContent = new MemoryStream(ValidDocumentBytes("unknown.pdf"));
        await documentStore.ImportAsync(new DocumentImport(long.MaxValue, "unknown.pdf", null, "SpecCheck", null), unknownContent);
        throw new InvalidOperationException("Document store accepted an unknown master record.");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("does not exist", StringComparison.Ordinal)) { }

    try
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await using var cancelledContent = new MemoryStream(ValidDocumentBytes("cancelled.pdf"));
        await documentStore.ImportAsync(new DocumentImport(first.MasterRecordId, "cancelled.pdf", null, "SpecCheck", null), cancelledContent, cancelled.Token);
        throw new InvalidOperationException("A cancelled document import completed.");
    }
    catch (OperationCanceledException) { }

    var cascadeMaster = await masterRepository.EnsureAsync("DOC-CASCADE", new DateOnly(2026, 8, 25));
    await ImportDocumentsAsync(documentStore, cascadeMaster.Id, ["cascade.pdf"]);
    await using (var cascadeConnection = database.Create())
    await using (var deleteCommand = cascadeConnection.CreateCommand())
    {
        deleteCommand.CommandText = "DELETE FROM master_record WHERE Id=@id";
        var parameter = deleteCommand.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = cascadeMaster.Id;
        deleteCommand.Parameters.Add(parameter);
        await deleteCommand.ExecuteNonQueryAsync();
    }
    if ((await documentStore.GetByMasterRecordIdsAsync([cascadeMaster.Id])).Count != 0)
        throw new InvalidOperationException("Deleting a master record did not cascade its customer-document associations.");

    var legalMaster = await masterRepository.EnsureAsync("LEGAL-DOC-BATCH", new DateOnly(2026, 8, 25), "L");
    var legalDocumentRecord = legalProvider.GetLegalEntity(legalMaster.CustomerId, LeConstitution.PrivateLimitedCompany);
    legalDocumentRecord.MasterRecordId = legalMaster.Id;
    await ImportDocumentsAsync(documentStore, legalMaster.Id, DocumentReferences.For(legalDocumentRecord));
    var legalDocumentBatch = await new CkycLegalEntityBatchGenerator(
        new BatchSettings { OutputRoot = Path.Combine(databaseTestRoot, "legal-document-batch") }, new FileHasher(), documentStore)
        .GenerateAsync([legalDocumentRecord], new DateOnly(2026, 8, 25));
    if (!File.Exists(legalDocumentBatch.ZipPath))
        throw new InvalidOperationException("Legal-entity batch was not generated from database documents.");

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
