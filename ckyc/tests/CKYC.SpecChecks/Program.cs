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

var adultWithoutRelatedParties = Read();
adultWithoutRelatedParties.RelatedParties.Clear();
AssertValid(adultWithoutRelatedParties, "adult without optional related parties");

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
if (lines.Any(line => line.StartsWith("60|", StringComparison.Ordinal)))
    throw new InvalidOperationException("An adult without related parties unexpectedly emitted record 60.");
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
// Widths per vendor/legal-format-create.xlsx validated against the real FVU utility:
// every detail line ends with an empty Hash Value placeholder (trailing pipe), and
// record 30 is constitution-specific (FVU ERR_169 pipe-count rule), so a company's
// record 30 carries exactly its own POI block + hash.
var legalWidths = new Dictionary<string, int>
{
    ["10"] = 11, ["20"] = 25, ["30"] = 11, ["40"] = 31, ["50"] = 12, ["60"] = 12, ["70"] = 21,
};
foreach (var line in legalLines)
{
    var fields = line.Split('|');
    if (!legalWidths.TryGetValue(fields[0], out var width) || fields.Length != width)
        throw new InvalidOperationException($"Legal record {fields[0]} emitted {fields.Length} fields; expected {width}.");
    if (!fields[0].Equals(CkycRecords.Header, StringComparison.Ordinal) && fields[^1] != "")
        throw new InvalidOperationException($"Legal record {fields[0]} did not end with the empty Hash Value placeholder.");
}
var relatedLines = legalLines.Where(line => line.StartsWith("60|", StringComparison.Ordinal)).Select(line => line.Split('|')).ToList();
if (relatedLines.Count != 2 || relatedLines.Any(fields => fields[3] != "2" || fields[4] != "1"))
    throw new InvalidOperationException("Legal record 60 did not emit related-person and beneficial-owner counts.");
// Controlling interest / percentage ownership are Beneficial Owner-only (ERR_111/ERR_258).
var directorLine = relatedLines.Single(fields => fields[5] == "Director");
if (directorLine[7] != "" || directorLine[8] != "")
    throw new InvalidOperationException("Controlling interest was emitted on a non-Beneficial-Owner related-party row.");
var ownerLine = relatedLines.Single(fields => fields[5] == "Beneficial Owner");
if (ownerLine[7] == "")
    throw new InvalidOperationException("The Beneficial Owner row did not carry its controlling interest.");
// Record 20 conditional flags must be blank when not applicable (ERR_252/ERR_257);
// a public limited company must still carry them.
var plCompany = legalProvider.GetLegalEntity("LEGAL-PL", LeConstitution.PublicLimitedCompany);
plCompany.ListedCompany = "Y"; plCompany.DateOfCommencement = "01-01-2016";
var plLines = new CkycLegalEntityUploadWriter(new BatchSettings())
    .Write([plCompany], new DateOnly(2026, 8, 25))
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
    .Single(line => line.StartsWith("20|", StringComparison.Ordinal)).Split('|');
if (plLines[5] != "Y")
    throw new InvalidOperationException("A public limited company lost its Listed Company flag.");

// Each other constitution emits its own narrowed record-30 block: trust and
// unincorporated association blocks have one field fewer than companies.
foreach (var (constitution, record30Width) in new[]
         {
             (LeConstitution.Trust, 10), (LeConstitution.UnincorporatedAssociation, 10),
             (LeConstitution.SoleProprietorship, 11), (LeConstitution.PartnershipFirm, 11),
         })
{
    var entity = legalProvider.GetLegalEntity($"LEGAL-R30-{constitution}", constitution);
    var constitutionLines = new CkycLegalEntityUploadWriter(new BatchSettings())
        .Write([entity], new DateOnly(2026, 8, 25))
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    var record30 = constitutionLines.Single(line => line.StartsWith("30|", StringComparison.Ordinal)).Split('|');
    if (record30.Length != record30Width || record30[^1] != "")
        throw new InvalidOperationException(
            $"Record 30 for constitution {constitution} emitted {record30.Length} fields; expected {record30Width} (+ empty Hash Value).");
}

var unsafeDocument = legalProvider.GetLegalEntity("LEGAL-UNSAFE", LeConstitution.PrivateLimitedCompany);
unsafeDocument.PanDocument = "../Pan.pdf";
if (!LegalEntityRecordValidator.Validate(unsafeDocument).Any(e => e.FieldName == "PAN document"))
    throw new InvalidOperationException("Legal validation accepted a document path traversal.");

// Record 20 CM rules from vendor/legal-format-create.xlsx.
var noPanNoForm97 = legalProvider.GetLegalEntity("LEGAL-NOPAN", LeConstitution.PartnershipFirm);
noPanNoForm97.Pan = null; noPanNoForm97.Form97 = null; noPanNoForm97.PanDocument = null;
var noPanErrors = LegalEntityRecordValidator.Validate(noPanNoForm97);
if (!noPanErrors.Any(e => e.FieldName == "Form 97"))
    throw new InvalidOperationException("Legal validation accepted a missing PAN without Form 97.");
if (!noPanErrors.Any(e => e.FieldName == "PAN/Form 97 document"))
    throw new InvalidOperationException("Legal validation accepted a missing PAN without the PAN/Form 97 document name.");
if (!noPanErrors.Any(e => e.FieldName == "PAN" && e.ErrorDescription!.Contains("mandatory", StringComparison.Ordinal)))
    throw new InvalidOperationException("Missing PAN did not report the partnership-firm mandatory-PAN rule.");

var badGst = legalProvider.GetLegalEntity("LEGAL-BADGST", LeConstitution.PrivateLimitedCompany);
badGst.TinGstNumber = "22ABCDE1234F1Z"; // 14 chars — must be 15 per GST format
if (!LegalEntityRecordValidator.Validate(badGst).Any(e => e.FieldName == "TIN/GST registration number"))
    throw new InvalidOperationException("Legal validation accepted a malformed TIN/GST number.");

var badCin = legalProvider.GetLegalEntity("LEGAL-BADCIN", LeConstitution.PrivateLimitedCompany);
badCin.Proofs[0].Cin = "L12345MH20PLC987654"; // missing one digit vs the 21-char CIN structure
if (!LegalEntityRecordValidator.Validate(badCin).Any(e => e.FieldName == "CIN"))
    throw new InvalidOperationException("Legal validation accepted a malformed CIN.");

// ERR_180: the fourth PAN character must match the constitution (T for trusts).
var trustBadPan = legalProvider.GetLegalEntity("LEGAL-PANCHR", LeConstitution.Trust);
trustBadPan.Pan = trustBadPan.Pan![0..3] + "C" + trustBadPan.Pan[4..]; // company character on a trust
if (!LegalEntityRecordValidator.Validate(trustBadPan).Any(e => e.FieldName == "PAN" && e.ErrorDescription!.Contains("fourth PAN character", StringComparison.Ordinal)))
    throw new InvalidOperationException("A trust PAN carrying a non-trust fourth character was accepted.");

// Record 40: same-as-registered Y means the principal block and its document are optional,
// while N requires both (the CRM default entity already exercises N).
var sameAddressCompany = legalProvider.GetLegalEntity("LEGAL-SAMEADDR", LeConstitution.PrivateLimitedCompany);
sameAddressCompany.PrincipalAddress = new LeAddressDetails { SameAsRegistered = "Y" };
sameAddressCompany.PrincipalAddressDocument = null;
if (LegalEntityRecordValidator.Validate(sameAddressCompany).Count != 0)
    throw new InvalidOperationException("Same-as-registered=Y rejected a valid registered-address-only record.");

// Record 50: an Indian mobile must be exactly 10 digits; emails must contain @.
var badContact = legalProvider.GetLegalEntity("LEGAL-BADCONTACT", LeConstitution.PrivateLimitedCompany);
badContact.Contact!.MobileNumber1 = "98765"; badContact.Contact.Email1 = "not-an-email";
var badContactErrors = LegalEntityRecordValidator.Validate(badContact);
if (!badContactErrors.Any(e => e.FieldName == "Mobile number (01)") || !badContactErrors.Any(e => e.FieldName == "Email ID (01)"))
    throw new InvalidOperationException("Legal validation accepted malformed mobile/email details.");

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
