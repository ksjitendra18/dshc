using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;

namespace CKYC.Crm;

/// <summary>
/// Real CRM client for the bank private gateway (CKYC2SFTP / CKYC2Retail* APIs). For each
/// customer it POSTs <c>{"custId": …, "identifier": "CRM"}</c> to the per-record-type
/// endpoints (20 demographic, 30 proof-of-identity, 40 address, 50 contact, 70 other
/// details) and maps the UPPER_SNAKE_CASE responses onto the <see cref="Individual"/>
/// domain model. Record 60 (related parties) is not served by the gateway and stays empty.
///
/// Response quirks handled here: keys arrive with mixed casing and some with literal
/// quote characters (<c>'KYCTYPE'</c>); dates arrive as <c>yyyy-MM-dd</c> (with an
/// optional time-of-day suffix) while the domain wants <c>dd-MM-yyyy</c>; numeric flags
/// (<c>PANVERIFIED: 1</c>) are normalised to Y/N; record 30 uses a different envelope
/// (<c>responseCode</c>/<c>records.CUR_DETAILS</c>) than the other sections
/// (<c>status</c>/<c>details</c>).
/// </summary>
public sealed class BankCrmApiClient : ICrmApiClient
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly CrmSettings _settings;

    public BankCrmApiClient(CrmSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) };
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(settings.BaseUrl);
        foreach (var (name, value) in settings.Headers)
            _http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
    }

    public Task<IReadOnlyList<string>> GetCustomerIdsAsync(CancellationToken ct = default)
        => throw new NotSupportedException(
            "The bank CRM gateway serves one customer per request (POST custId); " +
            "the daily customer ids come from the source step, not the CRM.");

    public async Task<Individual?> GetCustomerAsync(string customerId, CancellationToken ct = default)
    {
        // Demographics (20) is the core section — without it the gateway has no data for
        // the customer and null is returned so the caller can fail + retry the record.
        var demographics = await FetchDetailsAsync(_settings.DemographicsEndpoint, customerId, ct);
        if (demographics is null) return null;

        var individual = new Individual { CustomerId = customerId };
        MapDemographics(demographics.Value, individual);

        var poi = await FetchPoiAsync(_settings.PoiEndpoint, customerId, ct);
        if (poi is not null) individual.Proofs.AddRange(poi);

        var address = await FetchDetailsAsync(_settings.AddressEndpoint, customerId, ct);
        if (address is not null) MapAddress(address.Value, individual);

        var contact = await FetchDetailsAsync(_settings.ContactEndpoint, customerId, ct);
        if (contact is not null) MapContact(contact.Value, individual);

        var other = await FetchArrayAsync(_settings.OtherDetailsEndpoint, customerId, ct);
        if (other is not null && other.Count > 0) MapOther(other[0], individual);

        return individual;
    }

    // ---- Record 20 : Demographics ----

    private static void MapDemographics(JsonElement details, Individual r)
    {
        var d = Props(details);
        r.Name = NameOf(d, string.Empty);
        r.MaidenName = NameOf(d, "MAIDEN");
        r.MotherName = NameOf(d, "MOTHER");
        r.FatherName = NameOf(d, "FATHER");
        r.SpouseName = NameOf(d, "SPOUSE");

        r.DateOfBirth = ToDate(S(d, "DATE_OF_BIRTH"));
        r.Gender = S(d, "GENDER");
        r.ResidentialStatus = S(d, "RESIDENTIAL_STATUS");
        r.ResidentialStatusSupportedByDocument = S(d, "RESIDENTIAL_STATUS_SUPPORTED_WITH_DOCUMENT") ?? r.ResidentialStatusSupportedByDocument;
        r.Nationality = S(d, "NATIONALITY") ?? r.Nationality;
        r.NationalitySupportedByDocument = S(d, "NATIONALITY_SUPPORTED_WITH_DOCUMENT") ?? r.NationalitySupportedByDocument;

        r.Minor = S(d, "MINOR") ?? r.Minor;
        r.DateOfBirthMatchWithOvd = S(d, "DOB_MATCHING_WITH_OVD");
        r.NameMatchWithOvd = S(d, "NAME_MATCHING_WITH_OVD");
        r.PhotoProvidedMatchWithOvd = S(d, "PHOTO_PROVIDED_MATCHING_WITH_PHOTO_ON_THE_OVD");
        r.GenderProvidedInOvd = S(d, "GENDER_PROVIDED_IN_OVD");
        r.GenderMatchWithOvd = S(d, "GENDER_MATCHING_WITH_OVD");

        r.Pan = S(d, "PAN");
        r.PanVerified = ToYesNo(S(d, "PANVERIFIED"));
        r.PanDocument = S(d, "PAN_DOC");
        r.PhotoOfIndividual = S(d, "PHOTOOFINDIVIDUAL");

        r.Form61Provided = S(d, "FORM61");
        r.Form97Provided = S(d, "FORM97");

        r.DifferentlyAbledStatus = S(d, "PERSON_WITH_DISABILITY") ?? r.DifferentlyAbledStatus;
        // The disability sub-fields are conditional-mandatory: only required (and only
        // validated) when the PwD flag is Y — ignore the placeholder noise otherwise.
        if (r.DifferentlyAbledStatus == "Y")
        {
            r.DifferentlyAbledType = S(d, "TYPE_OF_IMPAIRMENT");
            r.OtherTypeOfImpairment = S(d, "OTHER_TYPE_OF_IMPAIRMENT");
            r.DisabilityReferenceNumber = S(d, "UDID_NUMBER");
            r.PermanentDisability = S(d, "PERSON_WITH_DISABILITY_STATUS_FLG");
            r.DisabilityDate = ToDate(S(d, "DISABILITYDATE"));
            r.PercentageOfImpairment = S(d, "PERCENTAGE_OF_IMPAIRMENT");
            r.DifferentlyAbledSupportedByDocument = S(d, "DIFFERENTLY_ABLED_STATUS_SUPPORTED_BY_DOCUMENT");
        }
    }

    private static PersonName NameOf(Dictionary<string, JsonElement> d, string prefix)
        => new()
        {
            // Related persons carry their title in <PREFIX>_NAME_PREFIX (FATHER_NAME_PREFIX, …);
            // the customer's own title is simply TITLE.
            Title = S(d, Key(prefix, "TITLE")) ?? S(d, Key(prefix, "NAME_PREFIX")) ?? string.Empty,
            FirstName = S(d, Key(prefix, "FIRST_NAME")) ?? string.Empty,
            MiddleName = S(d, Key(prefix, "MIDDLE_NAME")) ?? string.Empty,
            LastName = S(d, Key(prefix, "LAST_NAME")) ?? string.Empty,
        };

    /// <summary>The customer's own name has no prefix (<c>FIRST_NAME</c>), while the
    /// related-person names are prefixed (<c>FATHER_FIRST_NAME</c>, …).</summary>
    private static string Key(string prefix, string field)
        => string.IsNullOrEmpty(prefix) ? field : $"{prefix}_{field}";

    // ---- Record 30 : Proof of Identity (envelope: responseCode / records.CUR_DETAILS) ----

    private async Task<List<ProofOfIdentity>?> FetchPoiAsync(string endpoint, string customerId, CancellationToken ct)
    {
        var root = await PostRootAsync(endpoint, customerId, ct);
        if (root is null) return null;

        var props = Props(root.Value);
        if (!props.TryGetValue("responseCode", out var code) || Str(code) != "00")
            return null;

        if (!props.TryGetValue("records", out var records) || records.ValueKind != JsonValueKind.Object)
            return null;

        var recordProps = Props(records);
        if (!recordProps.TryGetValue("CUR_DETAILS", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;

        var proofs = new List<ProofOfIdentity>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var p = Props(item);
            proofs.Add(new ProofOfIdentity
            {
                OvdType = S(p, "OVD_TYPE") ?? string.Empty,
                ModeOfAadhaarVerification = S(p, "MODE_OF_AADHAAR_VERIFICATION") ?? string.Empty,
                PassportExpiryDate = ToDate(S(p, "PASSPORT_EXPIRY_DATE")),
                DrivingLicenseExpiryDate = ToDate(S(p, "DRIVING_LICENSE_EXPIRY_DATE")),
                LengthOfAadhaar = S(p, "LENGTH_OF_AADHAAR"),
                IdNumber = S(p, "IDENTITY_NUMBER"),
                CertifiedCopyWithOriginal = S(p, "CERTIFIED_COPY_VERIFIED_WITH_ORIGINAL_OVD"),
                EquivalentEDoc = S(p, "EQUIVALENT_EDOC"),
                VerifiedFromDigiLocker = S(p, "DOCUMENT_VERIFIED_FROM_DIGILOCKER"),
                PresenceInMeaRepository = Yn(p, "PRESENCE_OF_PASSPORT_IN_MEA_REPOSITORY"),
                PresenceInEciRepository = Yn(p, "PRESENCE_OF_VOTER_ID_IN_ECI_REPOSITORY"),
                PresenceInRtoRepository = Yn(p, "PRESENCE_OF_DRIVING_LICENSE_IN_RTO_REPOSITORY"),
                PresenceInNregaRepository = Yn(p, "PRESENCE_OF_NREGA_IN_RESPECTIVE_REPOSITORY"),
                PresenceInNprRecords = Yn(p, "PRESENCE_OF_NPR_IN_CENSUS_RECORDS"),
                DataFromOfflineVerification = Yn(p, "DATA_RECEIVED_FROM_OFFLINE_VERIFICATION"),
                ModeOfAuthentication = S(p, "MODE_OF_AUTHENTICATION"),
                EkycDataFromUidai = S(p, "EKYC_DATA_RECEIVED_FROM_UIDAI"),
                CopyOfOvd = S(p, "COPY_OF_OVD"),
            });
        }
        return proofs;
    }

    // ---- Record 40 : Addresses ----

    private static void MapAddress(JsonElement details, Individual r)
    {
        var d = Props(details);

        r.PermanentAddress = new AddressDetails
        {
            Line1 = S(d, "P_ADDRESS_L1") ?? string.Empty,
            Line2 = S(d, "P_ADDRESS_L2") ?? string.Empty,
            Line3 = S(d, "P_ADDRESS_L3") ?? string.Empty,
            Country = S(d, "P_COUNTRY") ?? string.Empty,
            State = S(d, "P_ADRS_STATE") ?? string.Empty,
            District = S(d, "P_DISTRICT") ?? string.Empty,
            City = S(d, "P_CITY") ?? string.Empty,
            PinCode = S(d, "P_PINCODE") ?? string.Empty,
            PinCodeOthers = S(d, "P_PINCODE_OTHERS") ?? S(d, "P_PINCOD_OTHERS"),
        };

        // The shared proof-of-address block (proof type, Aadhaar fields, verification
        // flags) describes whichever address is being evidenced — the current one.
        var current = new AddressDetails
        {
            Line1 = S(d, "C_ADDRESS_L1") ?? string.Empty,
            Line2 = S(d, "C_ADDRESS_L2") ?? string.Empty,
            Line3 = S(d, "C_ADDRESS_L3") ?? string.Empty,
            Country = S(d, "C_COUNTRY") ?? string.Empty,
            State = S(d, "C_ADRS_STATE") ?? string.Empty,
            District = S(d, "C_DISTRICT") ?? string.Empty,
            City = S(d, "C_CITY") ?? string.Empty,
            PinCode = S(d, "C_PINCODE") ?? string.Empty,
            PinCodeOthers = S(d, "C_PINCODE_OTHERS") ?? S(d, "C_PINCOD_OTHERS"),
        };
        ApplyProofOfAddress(d, current);

        r.CurrentAddressSameAsPermanent = S(d, "PERMANANT_ADRS_SAME") == "Y" ? "Y" : "N";
        r.CurrentAddress = current;
    }

    private static void ApplyProofOfAddress(Dictionary<string, JsonElement> d, AddressDetails a)
    {
        a.ProofOfAddress = S(d, "PROOFOFADDRESS");
        a.ProofOfAddressType = S(d, "ADDRESSTYPE");
        a.LengthOfAadhaar = S(d, "LENGTH_OF_AADHAAR");
        a.IdNumber = S(d, "IDENTITY_NUMBER");
        a.ModeOfAadhaarVerification = S(d, "MODEOFAADHAARVERIFICATION");
        a.OvdExpiryDate = ToDate(S(d, "EXPIRYDATE"));
        a.DeemedPoa = S(d, "DEEMEDPOA");
        a.DeemedPoaVerified = S(d, "DEEMEDPOAVERIFIED");
        a.CertifiedCopyWithOriginal = S(d, "CERTIFIEDCOPYVERIFIEDWITHORIGINALOVD");
        a.EquivalentEDoc = S(d, "EQUIVALENTEDOC");
        a.VerifiedFromDigiLocker = S(d, "DOCUMENTVERIFIEDFROMDIGILOCKER");
        a.RemoteGeoTagging = S(d, "REMOTEGEOTAGGING");
        a.AddressMatchWithOvd = S(d, "ADDRESS_MATCH_WITH_OVD") ?? a.AddressMatchWithOvd;
        a.AddressExactlyMatch = S(d, "ADDRESSEXACTLYMATCHWITHDEEMEDPOA");
        a.PositiveVerification = S(d, "POSITIVEVERIFICATIONOFCURRENTADDRESSTHROUGHLETTERORDELIVERIES");
        a.PhysicalVerificationByThirdParty = S(d, "PHYSICALVERIFICATIONBYTHIRDPARTY");
        a.PhysicalVerificationByReOfficial = S(d, "PHYSICALVERIFICATIONBYREOFFICIAL");
        a.PresenceInRepository = S(d, "PRESENCEOFDOCS");
        a.Digipin = S(d, "DIGIPIN");
        a.AddressSupportedWithDocument = S(d, "ADDRESS_SUPPORTED_WITH_DOCUMENT") ?? a.AddressSupportedWithDocument;
    }

    // ---- Record 50 : Contact ----

    private static void MapContact(JsonElement details, Individual r)
    {
        var d = Props(details);
        r.Contact = new ContactDetails
        {
            Email = S(d, "EMAIL_ID") ?? string.Empty,
            CountryCode = WithPlus(S(d, "COUNTRYCODE")) ?? "+91",
            MobileNumber = S(d, "MOBILENO") ?? string.Empty,
            MobileValidatedViaOtp = S(d, "MOBILENUMBERVALIDATEDTHROUGHOTP"),
            EmailValidatedViaOtp = S(d, "EMAILVALIDATEDTHROUGHOTP"),
            MobileValidatedViaThirdParty = S(d, "MOBILENUMBERVALIDATEDTHROUGHTHIRDPARTY"),
        };
    }

    // ---- Record 70 : Other Details & Attestation (details is an array) ----

    private static void MapOther(JsonElement details, Individual r)
    {
        var d = Props(details);
        r.Other = new OtherDetails
        {
            Remarks = S(d, "REMARKS") ?? string.Empty,
            VideoKycWithoutOfficial = S(d, "VIDEOKYCWITHOUTOFFICIAL") ?? "N",
            VideoKycWithReOfficial = S(d, "VIDEOKYCWITHREOFFICIAL") ?? "N",
            FaceToFaceWithReOfficial = S(d, "FACETOFACEWITHREOFFICIAL") ?? "N",
            FaceToFaceWithNonOfficial = S(d, "FACETOFACEWITHNONOFFICIALBC") ?? "N",
            NonFaceToFace = S(d, "NONFACETOFACE") ?? "N",
            AttestationDate = ToDate(S(d, "ATTESTATIONDATE")) ?? string.Empty,
            EmployeeName = S(d, "EMPLOYEENAME") ?? string.Empty,
            EmployeeCode = S(d, "EMPLOYEECODE") ?? string.Empty,
            EmployeeDesignation = S(d, "EMP_DESIGNATION") ?? string.Empty,
            EmployeeBranch = S(d, "EMP_BRANCH") ?? string.Empty,
            EmployeeCkycId = S(d, "EMP_CKYCID") ?? string.Empty,
            InstitutionName = S(d, "INSTITUTION_NAME") ?? string.Empty,
            InstitutionCode = S(d, "INSTITUTION_CODE") ?? string.Empty,
            DeclarationDocument = S(d, "DECLARATIONDOCUMENT") ?? "D1.pdf",   // generated at batch time (documentGeneration config)
            DeclarationFlag = S(d, "DECLARATION_FLAG") ?? "Y",
            ClientConsent = S(d, "CLIENTCONSENT") ?? "C3.pdf",               // generated at batch time (documentGeneration config)
            Place = S(d, "PLACE") ?? string.Empty,
            DeclarationDate = ToDate(S(d, "DECLARATION_DATE")) ?? string.Empty,
        };
    }

    // ---- Transport + envelope handling ----

    /// <summary>POSTs the custId request and returns the <c>details</c> object when the
    /// envelope reports success (<c>status: "S"</c>); null when it does not.</summary>
    private async Task<JsonElement?> FetchDetailsAsync(string endpoint, string customerId, CancellationToken ct)
    {
        var root = await PostRootAsync(endpoint, customerId, ct);
        if (root is null || root.Value.ValueKind != JsonValueKind.Object) return null;

        var props = Props(root.Value);
        if (!IsSuccess(props)) return null;
        return props.TryGetValue("details", out var details) && details.ValueKind == JsonValueKind.Object
            ? details
            : null;
    }

    /// <summary>POSTs the custId request and returns the <c>details</c> array when the
    /// envelope reports success (<c>status: "S"</c>); null when it does not.</summary>
    private async Task<List<JsonElement>?> FetchArrayAsync(string endpoint, string customerId, CancellationToken ct)
    {
        var root = await PostRootAsync(endpoint, customerId, ct);
        if (root is null || root.Value.ValueKind != JsonValueKind.Object) return null;

        var props = Props(root.Value);
        if (!IsSuccess(props)) return null;
        if (!props.TryGetValue("details", out var details) || details.ValueKind != JsonValueKind.Array)
            return null;

        var items = new List<JsonElement>();
        foreach (var item in details.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object) items.Add(item.Clone());
        return items;
    }

    private async Task<JsonElement?> PostRootAsync(string endpoint, string customerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        var response = await _http.PostAsJsonAsync(
            endpoint.TrimStart('/'),
            new { custId = customerId, identifier = _settings.Identifier },
            WriteOptions, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    private static bool IsSuccess(Dictionary<string, JsonElement> props)
    {
        if (!props.TryGetValue("status", out var status)) return true;
        var value = Str(status);
        if (value is null) return true;
        if (string.Equals(value, "S", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ---- Value helpers ----

    /// <summary>Case-insensitive property lookup with the stray quote characters
    /// (<c>'FIELD'</c>) stripped from the names.</summary>
    private static Dictionary<string, JsonElement> Props(JsonElement obj)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj.EnumerateObject())
            dict[property.Name.Trim('\'').Trim()] = property.Value;
        return dict;
    }

    /// <summary>Reads a non-empty trimmed string (numbers are stringified); null when the
    /// key is missing, null or blank.</summary>
    private static string? S(Dictionary<string, JsonElement> d, string key)
    {
        if (!d.TryGetValue(key, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => Str(value),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "Y",
            JsonValueKind.False => "N",
            _ => null,
        };
    }

    private static string? Str(JsonElement value)
    {
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>Reads a Y/N flag; the bank sends "NA" when a flag is not applicable,
    /// which the record spec treats as absent.</summary>
    private static string? Yn(Dictionary<string, JsonElement> d, string key)
        => S(d, key) is { } value && !value.Equals("NA", StringComparison.OrdinalIgnoreCase) ? value : null;

    /// <summary>Normalises numeric flags (<c>1</c>/<c>0</c>) and booleans to Y/N.</summary>
    private static string? ToYesNo(string? value) => value switch
    {
        "1" => "Y",
        "0" => "N",
        null => null,
        _ => value,
    };

    private static readonly string[] BankDateFormats =
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.f", "yyyy-MM-dd HH:mm:ss.fff"];

    /// <summary>Converts the bank date formats ("yyyy-MM-dd", optionally with a time-of-day
    /// suffix) to the dd-MM-yyyy form the record spec requires; dd-MM-yyyy passes through.</summary>
    private static string? ToDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        if (DateTime.TryParseExact(text, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.ToString("dd-MM-yyyy");
        if (DateTime.TryParseExact(text, BankDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed.ToString("dd-MM-yyyy");
        return text;
    }

    private static string? WithPlus(string? code)
        => string.IsNullOrWhiteSpace(code) ? code : (code.StartsWith('+') ? code : "+" + code);
}
