using System.Globalization;
using System.Text.RegularExpressions;
using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Core.Spec;

/// <summary>
/// Validates a single <see cref="Individual"/> against the CERSAI CKYC file-format
/// rules (the "File_Format_Upload_Individual" Excel). It focuses on the
/// conditional-mandatory (CM) fields — fields that become mandatory only when
/// another field takes a particular value — returning one <see cref="ValidationError"/>
/// per rule that is not satisfied.
///
/// This is a pre-flight gate for step 4 (build-zip): a record with any violation is
/// excluded from the batch and its errors are surfaced, instead of shipping a .UPL
/// that the FVU would reject.
/// </summary>
public sealed class CkycRecordValidator
{
    private const string Record20 = "20";
    private const string Record30 = "30";
    private const string Record40 = "40";
    private const string Record50 = "50";
    private const string Record60 = "60";
    private const string Record70 = "70";
    private static readonly Regex NamePattern = new(@"^[A-Za-z'.]+$", RegexOptions.CultureInvariant);
    private static readonly Regex PanPattern = new(@"^[A-Z]{3}P[A-Z][0-9]{4}[A-Z]$", RegexOptions.CultureInvariant);
    private static readonly Regex DigitsPattern = new(@"^[0-9]+$", RegexOptions.CultureInvariant);

    private static bool Is(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns every rule violation for the record, or an empty list when it is valid.</summary>
    public static IReadOnlyList<ValidationError> Validate(Individual r)
    {
        var errors = new List<ValidationError>();

        ValidateRecord20(r, errors);

        if (Is(r.KycType, "O") && r.Proofs.Count != 1)
            errors.Add(Error(Record30, null, "OVD Details", r.Proofs.Count.ToString(),
                "KYC type O must contain exactly one OVD record."));

        foreach (var p in r.Proofs)
            errors.AddRange(ValidateRecord30(r, p));

        if (r.Proofs.Count == 0)
            errors.Add(Error(Record30, null, "OVD Details", null, "At least one record type 30 (Proof of Identity & Address) is required."));

        errors.AddRange(ValidateRecord40(r));

        // Record type 50 is optional: both email and mobile are O in the create format.
        // Its CM fields apply only when the corresponding optional value is supplied.
        if (r.Contact is not null) errors.AddRange(ValidateRecord50(r.Contact));

        errors.AddRange(EachRelatedParty(r));

        if (r.Other is null)
            errors.Add(Error(Record70, null, "Other Details & Attestation", null, "Record type 70 (Other Details & Attestation) is required."));
        else
            errors.AddRange(ValidateRecord70(r, r.Other));

        return errors;
    }

    private static IEnumerable<ValidationError> EachRelatedParty(Individual r)
    {
        // Record type 60 is optional except for a client below ten years of age.
        if (IsBelowTen(r.DateOfBirth) && r.RelatedParties.Count == 0)
            yield return Error(Record60, null, "Related Party Details", null,
                "Guardian details are mandatory for a client below 10 years of age.");

        foreach (var rp in r.RelatedParties)
        {
            if (string.IsNullOrWhiteSpace(rp.RelatedPersonType))
                yield return Error(Record60, null, "Related Person Type", rp.RelatedPersonType,
                    "Related Person Type (Guardian / Assignee / Authorized Representative) is required.");

            if (!string.IsNullOrWhiteSpace(rp.RelatedPersonType) && string.IsNullOrWhiteSpace(rp.CkycNumberOfRelatedPerson))
                yield return Error(Record60, null, "CKYC Number of Related Person", rp.CkycNumberOfRelatedPerson,
                    "CKYC Number of Related Person is required when Related Person Type is provided.");
        }
    }

    private static void ValidateRecord20(Individual r, List<ValidationError> errors)
    {
        Require(errors, Record20, "Search Key", r.SearchKey, "Search Key is mandatory.");
        if (!string.IsNullOrWhiteSpace(r.SearchKey) && r.SearchKey.Length != 20)
            errors.Add(Error(Record20, null, "Search Key", r.SearchKey, "Search Key must be exactly 20 characters."));

        RequireAllowed(errors, Record20, "KYC Type", r.KycType, ["N", "M", "S", "O"]);
        RequireAllowed(errors, Record20, "Title", r.Name.Title, ["Mr", "Mr.", "Ms", "Ms.", "Mrs", "Mrs.", "Mx", "Mx."]);

        if (string.IsNullOrWhiteSpace(r.Name.FirstName))
            errors.Add(Error(Record20, null, "First Name", r.Name.FirstName, "Name (First Name) is mandatory."));
        else
        {
            if (r.Name.FirstName.Length > 33)
                errors.Add(Error(Record20, null, "First Name", r.Name.FirstName, "First Name cannot exceed 33 characters."));
            if (!NamePattern.IsMatch(r.Name.FirstName))
                errors.Add(Error(Record20, null, "First Name", r.Name.FirstName,
                    "First Name permits letters, apostrophe and dot only; spaces are not allowed."));
        }

        if (string.IsNullOrWhiteSpace(r.DateOfBirth))
            errors.Add(Error(Record20, null, "Date of Birth", r.DateOfBirth, "Date of Birth is mandatory."));
        else if (!IsDate(r.DateOfBirth))
            errors.Add(Error(Record20, null, "Date of Birth", r.DateOfBirth,
                "Date of Birth must use DD-MM-YYYY format and be a valid date."));

        RequireAllowed(errors, Record20, "Minor", r.Minor, ["Y", "N"]);
        RequireAllowed(errors, Record20, "DOB matching with OVD", r.DateOfBirthMatchWithOvd, ["Y", "N"]);
        RequireAllowed(errors, Record20, "Name matching with OVD", r.NameMatchWithOvd, ["Y", "N"]);
        RequireAllowed(errors, Record20, "Photo provided matching with OVD", r.PhotoProvidedMatchWithOvd, ["Y", "N"]);
        RequireAllowed(errors, Record20, "Gender", r.Gender, ["M", "F", "T"]);
        RequireAllowed(errors, Record20, "Gender provided in OVD", r.GenderProvidedInOvd, ["Y", "N"]);
        RequireAllowed(errors, Record20, "Residential Status", ResidentialStatusValue(r.ResidentialStatus), ["A", "B", "C", "D"]);
        RequireAllowed(errors, Record20, "Residential Status supported with document", r.ResidentialStatusSupportedByDocument, ["Y", "N"]);
        RequireAllowed(errors, Record20, "Person with Disability (PwD)", r.DifferentlyAbledStatus, ["Y", "N"]);
        Require(errors, Record20, "Photo of Individual", r.PhotoOfIndividual, "Photo of Individual is mandatory.");

        if (Is(r.KycType, "O") && Is(r.Minor, "Y"))
            errors.Add(Error(Record20, null, "KYC Type", r.KycType,
                "KYC type O is not applicable to a minor."));

        // At least one of Mother / Father / Spouse name (conditional-mandatory).
        if (!r.MotherName.HasAnyName && !r.FatherName.HasAnyName && !r.SpouseName.HasAnyName)
            errors.Add(Error(Record20, null, "Mother / Father / Spouse Name", null,
                "At least one of Mother Name, Father Name or Spouse Name must be provided."));
        ValidateRelatedName(r.MotherName, "Mother Name", errors);
        ValidateRelatedName(r.FatherName, "Father Name", errors);
        ValidateRelatedName(r.SpouseName, "Spouse Name", errors);

        // Gender matching with OVD — mandatory when "Gender provided in OVD" = Y.
        if (Is(r.GenderProvidedInOvd, "Y") && string.IsNullOrWhiteSpace(r.GenderMatchWithOvd))
            errors.Add(Error(Record20, null, "Gender matching with OVD", r.GenderMatchWithOvd,
                "Gender matching with OVD is mandatory when Gender provided in OVD is Y."));
        else if (!string.IsNullOrWhiteSpace(r.GenderMatchWithOvd) && !IsOneOf(r.GenderMatchWithOvd, ["Y", "N"]))
            errors.Add(Error(Record20, null, "Gender matching with OVD", r.GenderMatchWithOvd,
                "Gender matching with OVD must be Y or N."));

        // One of PAN / Form 97 (erstwhile Form 60) / Form 61 is required.
        var panOrForm = !string.IsNullOrWhiteSpace(r.Pan) || Is(r.Form97Provided, "Y") || Is(r.Form61Provided, "Y");
        if (!panOrForm)
            errors.Add(Error(Record20, null, "PAN / Form 97 / Form 61", r.Pan,
                "Any one from PAN, Form 97 (erstwhile Form 60) or Form 61 is required."));
        OptionalAllowed(errors, Record20, "Form 97", r.Form97Provided, ["Y", "N"]);
        OptionalAllowed(errors, Record20, "Form 61", r.Form61Provided, ["Y", "N"]);

        // PAN verified — mandatory where PAN is provided.
        if (!string.IsNullOrWhiteSpace(r.Pan) && string.IsNullOrWhiteSpace(r.PanVerified))
            errors.Add(Error(Record20, null, "PAN verified", r.PanVerified, "PAN verified is mandatory when PAN is provided."));
        if (!string.IsNullOrWhiteSpace(r.Pan) && !PanPattern.IsMatch(r.Pan.Trim().ToUpperInvariant()))
            errors.Add(Error(Record20, null, "PAN", r.Pan,
                "PAN must match AAAAA9999A and its fourth character must be P."));
        if (!string.IsNullOrWhiteSpace(r.PanVerified) && !IsOneOf(r.PanVerified, ["Y", "N"]))
            errors.Add(Error(Record20, null, "PAN verified", r.PanVerified, "PAN verified must be Y or N."));

        // Disability detail fields — mandatory when Person with Disability (PwD) = Y.
        if (Is(r.DifferentlyAbledStatus, "Y"))
        {
            if (string.IsNullOrWhiteSpace(r.DifferentlyAbledType))
                errors.Add(Error(Record20, null, "Type of Impairment", r.DifferentlyAbledType,
                    "Type of Impairment is mandatory when Person with Disability (PwD) is Y."));

            if (string.IsNullOrWhiteSpace(r.DisabilityReferenceNumber))
                errors.Add(Error(Record20, null, "Disability Reference Number", r.DisabilityReferenceNumber,
                    "Certificate of Disability reference number / UDID Card Number is mandatory when Person with Disability (PwD) is Y."));

            if (string.IsNullOrWhiteSpace(r.PermanentDisability))
                errors.Add(Error(Record20, null, "Permanent Disability", r.PermanentDisability,
                    "Permanent disability is mandatory when Person with Disability (PwD) is Y."));

            if (string.IsNullOrWhiteSpace(r.PercentageOfImpairment))
                errors.Add(Error(Record20, null, "Percentage of Impairment", r.PercentageOfImpairment,
                    "Percentage of Impairment is mandatory when Person with Disability (PwD) is Y."));

            if (string.IsNullOrWhiteSpace(r.DifferentlyAbledSupportedByDocument))
                errors.Add(Error(Record20, null, "PwD supported with document", r.DifferentlyAbledSupportedByDocument,
                    "Person with Disability (PwD) supported with document is mandatory when Person with Disability (PwD) is Y."));
        }

        // "Other Type of Impairment" — mandatory when Type of Impairment = Others (21).
        if (Is(r.DifferentlyAbledType, "21") && string.IsNullOrWhiteSpace(r.OtherTypeOfImpairment))
            errors.Add(Error(Record20, null, "Other Type of Impairment", r.OtherTypeOfImpairment,
                "Other Type of Impairment is mandatory when Type of Impairment is Others (21)."));

        // Disability date — mandatory when Permanent disability = N.
        if (Is(r.PermanentDisability, "N") && string.IsNullOrWhiteSpace(r.DisabilityDate))
            errors.Add(Error(Record20, null, "Disability Date", r.DisabilityDate,
                "Disability date is mandatory when Permanent disability is N."));

        if (!string.IsNullOrWhiteSpace(r.DisabilityDate) && !IsDate(r.DisabilityDate))
            errors.Add(Error(Record20, null, "Disability Date", r.DisabilityDate,
                "Disability Date must use DD-MM-YYYY format and be a valid date."));
        if (!string.IsNullOrWhiteSpace(r.PercentageOfImpairment)
            && (!int.TryParse(r.PercentageOfImpairment, out var percentage) || percentage is < 1 or > 100))
            errors.Add(Error(Record20, null, "Percentage of Impairment", r.PercentageOfImpairment,
                "Percentage of Impairment must be a number from 01 through 100."));
    }

    private static List<ValidationError> ValidateRecord30(Individual r, ProofOfIdentity p)
    {
        var errors = new List<ValidationError>();

        // OVD Type is mandatory.
        if (string.IsNullOrWhiteSpace(p.OvdType))
            errors.Add(Error(Record30, null, "OVD Type", p.OvdType, "OVD Type is mandatory."));

        if (string.IsNullOrWhiteSpace(p.OvdType))
            return errors;

        var ovd = p.OvdType.Trim().ToUpperInvariant();
        var kycO = Is(r.KycType, "O");

        if (!IsOneOf(ovd, ["A", "B", "D", "E", "F", "G", "H"]))
            errors.Add(Error(Record30, null, "OVD Type", p.OvdType,
                "OVD Type must be A, B, D, E, F, G or H."));
        if (kycO && ovd != "E")
            errors.Add(Error(Record30, null, "OVD Type", p.OvdType,
                "Aadhaar/VID (E) is mandatory for KYC type O."));
        if (ovd == "H" && !Is(r.KycType, "S"))
            errors.Add(Error(Record30, null, "OVD Type", p.OvdType,
                "OVD Type H is allowed only for a Small Account (S)."));
        if (Is(r.KycType, "M") && ovd is not ("A" or "E"))
            errors.Add(Error(Record30, null, "OVD Type", p.OvdType,
                "Only Passport (A) or Aadhaar/VID (E) is allowed for a Minor Account (M)."));

        // Mode of Aadhaar Verification applies whenever Aadhaar/VID is selected;
        // KYC type O specifically requires e-KYC Authentication (B).
        if (ovd == "E" && string.IsNullOrWhiteSpace(p.ModeOfAadhaarVerification))
            errors.Add(Error(Record30, null, "Mode of Aadhaar Verification", p.ModeOfAadhaarVerification,
                "Mode of Aadhaar Verification is mandatory for an Aadhaar/VID OVD."));
        else if (ovd == "E" && !IsOneOf(p.ModeOfAadhaarVerification, ["A", "B", "C"]))
            errors.Add(Error(Record30, null, "Mode of Aadhaar Verification", p.ModeOfAadhaarVerification,
                "Mode of Aadhaar Verification must be A, B or C."));
        if (kycO && ovd == "E" && !Is(p.ModeOfAadhaarVerification, "B"))
            errors.Add(Error(Record30, null, "Mode of Aadhaar Verification", p.ModeOfAadhaarVerification,
                "E-KYC Authentication (B) is mandatory for KYC type O."));

        // Passport expiry date — required when Passport (A) is the OVD.
        if (ovd == "A" && string.IsNullOrWhiteSpace(p.PassportExpiryDate))
            errors.Add(Error(Record30, null, "Passport expiry date", p.PassportExpiryDate,
                "Passport expiry date is required when the OVD is a Passport."));

        // Driving licence expiry date — required when Driving Licence (D) is the OVD.
        if (ovd == "D" && string.IsNullOrWhiteSpace(p.DrivingLicenseExpiryDate))
            errors.Add(Error(Record30, null, "Driving licence expiry date", p.DrivingLicenseExpiryDate,
                "Driving licence expiry date is required when the OVD is a Driving Licence."));
        if (!string.IsNullOrWhiteSpace(p.PassportExpiryDate) && !IsCompactDate(p.PassportExpiryDate))
            errors.Add(Error(Record30, null, "Passport expiry date", p.PassportExpiryDate,
                "Passport expiry date must use DDMMYYYY format."));
        if (!string.IsNullOrWhiteSpace(p.DrivingLicenseExpiryDate) && !IsCompactDate(p.DrivingLicenseExpiryDate))
            errors.Add(Error(Record30, null, "Driving licence expiry date", p.DrivingLicenseExpiryDate,
                "Driving licence expiry date must use DDMMYYYY format."));

        // Length of Aadhaar/VID — applicable for OVD type E only.
        if (ovd == "E" && string.IsNullOrWhiteSpace(p.LengthOfAadhaar))
            errors.Add(Error(Record30, null, "Length of Aadhaar/VID", p.LengthOfAadhaar,
                "Length of Aadhaar/VID is required for OVD type E."));
        else if (ovd == "E" && !Is(p.LengthOfAadhaar, "A"))
            errors.Add(Error(Record30, null, "Length of Aadhaar/VID", p.LengthOfAadhaar,
                "Length of Aadhaar/VID must be A (four-digit masked Aadhaar)."));

        // ID Number — applicable for all OVD types except "H".
        if (ovd != "H" && string.IsNullOrWhiteSpace(p.IdNumber))
            errors.Add(Error(Record30, null, "ID Number", p.IdNumber, "ID Number is required for all OVD types except ID not available (H)."));
        if (ovd == "E" && !string.IsNullOrWhiteSpace(p.IdNumber)
            && (p.IdNumber.Length != 4 || !DigitsPattern.IsMatch(p.IdNumber)))
            errors.Add(Error(Record30, null, "ID Number", p.IdNumber,
                "A masked Aadhaar ID Number must contain exactly four digits."));

        // Copy of OVD — applicable for all OVD types except "H".
        if (ovd != "H" && string.IsNullOrWhiteSpace(p.CopyOfOvd))
            errors.Add(Error(Record30, null, "Copy of OVD", p.CopyOfOvd, "Copy of OVD file is required for all OVD types except ID not available (H)."));

        // At least one of certified copy / equivalent e-doc / DigiLocker — applicable for all OVD
        // types except "H" and except Aadhaar + (E-KYC or Offline) verification.
        var aadhaarEkycOrOffline = ovd == "E" &&
            (Is(p.ModeOfAadhaarVerification, "B") || Is(p.ModeOfAadhaarVerification, "C"));
        var atLeastOneProof = Is(p.CertifiedCopyWithOriginal, "Y") || Is(p.EquivalentEDoc, "Y") || Is(p.VerifiedFromDigiLocker, "Y");
        if (ovd != "H" && !aadhaarEkycOrOffline && !atLeastOneProof)
            errors.Add(Error(Record30, null, "Certified copy / equivalent e-doc / DigiLocker", null,
                "At least one of Certified copy matched with original OVD, Equivalent e-doc, or Document verified from DigiLocker is mandatory."));
        OptionalAllowed(errors, Record30, "Certified copy verified with original OVD", p.CertifiedCopyWithOriginal, ["Y", "N"]);
        OptionalAllowed(errors, Record30, "Equivalent e-doc", p.EquivalentEDoc, ["Y", "N"]);
        OptionalAllowed(errors, Record30, "Document verified from DigiLocker", p.VerifiedFromDigiLocker, ["Y", "N"]);

        // Repository presence flags — applicable per OVD type.
        if (ovd == "A" && string.IsNullOrWhiteSpace(p.PresenceInMeaRepository))
            errors.Add(Error(Record30, null, "Presence of Passport in MEA repository", p.PresenceInMeaRepository,
                "Presence of Passport in MEA repository is required for a Passport OVD."));
        if (ovd == "B" && string.IsNullOrWhiteSpace(p.PresenceInEciRepository))
            errors.Add(Error(Record30, null, "Presence of Voter ID in ECI repository", p.PresenceInEciRepository,
                "Presence of Voter ID in ECI repository is required for a Voter ID OVD."));
        if (ovd == "D" && string.IsNullOrWhiteSpace(p.PresenceInRtoRepository))
            errors.Add(Error(Record30, null, "Presence of Driving Licence in RTO repository", p.PresenceInRtoRepository,
                "Presence of Driving Licence in RTO repository is required for a Driving Licence OVD."));
        if (ovd == "F" && string.IsNullOrWhiteSpace(p.PresenceInNregaRepository))
            errors.Add(Error(Record30, null, "Presence of NREGA in respective repository", p.PresenceInNregaRepository,
                "Presence of NREGA in respective repository is required for an NREGA OVD."));
        if (ovd == "G" && string.IsNullOrWhiteSpace(p.PresenceInNprRecords))
            errors.Add(Error(Record30, null, "Presence of NPR in census records", p.PresenceInNprRecords,
                "Presence of NPR in census records is required for an NPR OVD."));
        OptionalAllowed(errors, Record30, "Presence of Passport in MEA repository", p.PresenceInMeaRepository, ["Y", "N"]);
        OptionalAllowed(errors, Record30, "Presence of Voter ID in ECI repository", p.PresenceInEciRepository, ["Y", "N"]);
        OptionalAllowed(errors, Record30, "Presence of Driving Licence in RTO repository", p.PresenceInRtoRepository, ["Y", "N"]);
        OptionalAllowed(errors, Record30, "Presence of NREGA in respective repository", p.PresenceInNregaRepository, ["Y", "N"]);
        OptionalAllowed(errors, Record30, "Presence of NPR in census records", p.PresenceInNprRecords, ["Y", "N"]);

        // Mode of Authentication — applicable for Aadhaar OVD in E-KYC mode.
        if (ovd == "E" && Is(p.ModeOfAadhaarVerification, "B") && string.IsNullOrWhiteSpace(p.ModeOfAuthentication))
            errors.Add(Error(Record30, null, "Mode of Authentication", p.ModeOfAuthentication,
                "Mode of Authentication is required for Aadhaar OVD in E-KYC mode."));
        else if (!string.IsNullOrWhiteSpace(p.ModeOfAuthentication) && !IsOneOf(p.ModeOfAuthentication, ["A", "B", "C"]))
            errors.Add(Error(Record30, null, "Mode of Authentication", p.ModeOfAuthentication,
                "Mode of Authentication must be A, B or C."));

        if (ovd == "E" && Is(p.ModeOfAadhaarVerification, "C") && string.IsNullOrWhiteSpace(p.DataFromOfflineVerification))
            errors.Add(Error(Record30, null, "Data received from offline verification", p.DataFromOfflineVerification,
                "Data received from offline verification is mandatory when Aadhaar offline verification is selected."));

        // E-KYC data received from UIDAI — mandatory for KYC type O / Aadhaar E-KYC.
        if (ovd == "E" && (kycO || Is(p.ModeOfAadhaarVerification, "B")) && string.IsNullOrWhiteSpace(p.EkycDataFromUidai))
            errors.Add(Error(Record30, null, "E-KYC data received from UIDAI", p.EkycDataFromUidai,
                "E-KYC data received from UIDAI is mandatory for KYC type O or Aadhaar E-KYC."));
        OptionalAllowed(errors, Record30, "Data received from offline verification", p.DataFromOfflineVerification, ["Y", "N"]);
        OptionalAllowed(errors, Record30, "E-KYC data received from UIDAI", p.EkycDataFromUidai, ["Y", "N"]);

        return errors;
    }

    private static List<ValidationError> ValidateRecord40(Individual r)
    {
        var errors = new List<ValidationError>();

        if (r.PermanentAddress is null)
        {
            errors.Add(Error(Record40, null, "Permanent Address", null, "Permanent Address is mandatory."));
        }
        else
        {
            ValidateAddressBlock(r.PermanentAddress, "Permanent Address", Record40, errors,
                supportRequired: true,
                addressMatchRequired: r.Proofs.Any(p => !Is(p.OvdType, "H")));
        }

        var current = r.CurrentAddress;
        if (current is not null && !SameAddress(r.PermanentAddress, current))
        {
            ValidateAddressBlock(current, "Current Address", Record40, errors,
                supportRequired: false, addressMatchRequired: false);
            ValidateCurrentAddressProof(r, current, errors);
        }

        return errors;
    }

    private static void ValidateAddressBlock(
        AddressDetails a,
        string section,
        string recordType,
        List<ValidationError> errors,
        bool supportRequired,
        bool addressMatchRequired)
    {
        if (string.IsNullOrWhiteSpace(a.Line1))
            errors.Add(Error(recordType, null, $"{section} Line 1", a.Line1,
                $"{section} (Flat No / House No) is mandatory."));

        // Country-aware CM fields: State/District/City/PinCode become mandatory when Country = IN.
        Require(errors, recordType, $"{section} Country", a.Country, $"{section} Country is mandatory.");
        var isIndia = Is(a.Country, "IN");
        if (isIndia)
        {
            if (string.IsNullOrWhiteSpace(a.State))
                errors.Add(Error(recordType, null, $"{section} State / UT", a.State,
                    $"{section} State / UT is mandatory when Country is India (IN)."));
            if (string.IsNullOrWhiteSpace(a.District))
                errors.Add(Error(recordType, null, $"{section} District", a.District,
                    $"{section} District is mandatory when Country is India (IN)."));
            if (string.IsNullOrWhiteSpace(a.City))
                errors.Add(Error(recordType, null, $"{section} City", a.City,
                    $"{section} City is mandatory when Country is India (IN)."));
            if (string.IsNullOrWhiteSpace(a.PinCode))
                errors.Add(Error(recordType, null, $"{section} Pin Code", a.PinCode,
                    $"{section} Pin Code is mandatory when Country is India (IN)."));
        }

        // Address supported with document / address match with OVD are mandatory.
        if (supportRequired && string.IsNullOrWhiteSpace(a.AddressSupportedWithDocument))
            errors.Add(Error(recordType, null, $"{section} address supported with document", a.AddressSupportedWithDocument,
                $"{section} address supported with document is mandatory."));
        if (addressMatchRequired && string.IsNullOrWhiteSpace(a.AddressMatchWithOvd))
            errors.Add(Error(recordType, null, $"{section} Address match with OVD", a.AddressMatchWithOvd,
                $"{section} Address match with OVD is mandatory."));
    }

    private static void ValidateCurrentAddressProof(Individual r, AddressDetails a, List<ValidationError> errors)
    {
        if (!Is(a.Country, "IN"))
        {
            Require(errors, Record40, "Current Address State / UT", a.State,
                "Current Address State / UT is mandatory when the current address differs.");
            Require(errors, Record40, "Current Address District", a.District,
                "Current Address District is mandatory when the current address differs.");
            Require(errors, Record40, "Current Address City", a.City,
                "Current Address City is mandatory when the current address differs.");
            Require(errors, Record40, "Current Address Pin Code", a.PinCode,
                "Current Address Pin Code is mandatory when the current address differs.");
        }

        RequireAllowed(errors, Record40, "Proof of Address", a.ProofOfAddress, ["1", "2", "3"]);
        var ovd = Is(a.ProofOfAddress, "1");
        var deemed = Is(a.ProofOfAddress, "2");

        if (ovd)
            RequireAllowed(errors, Record40, "Proof of Address Type", a.ProofOfAddressType,
                ["A", "B", "D", "E", "F", "G", "H"]);
        if (ovd && Is(a.ProofOfAddressType, "E"))
        {
            RequireAllowed(errors, Record40, "Length of Aadhaar/VID", a.LengthOfAadhaar, ["A"]);
            RequireAllowed(errors, Record40, "Mode of Aadhaar Verification", a.ModeOfAadhaarVerification, ["A", "B", "C"]);
        }
        if (ovd && !Is(a.ProofOfAddressType, "H"))
        {
            Require(errors, Record40, "Current Address ID Number", a.IdNumber,
                "ID Number is mandatory for an OVD other than H.");
            RequireAllowed(errors, Record40, "Certified copy verified with original OVD", a.CertifiedCopyWithOriginal, ["Y", "N"]);
            RequireAllowed(errors, Record40, "Document verified from DigiLocker", a.VerifiedFromDigiLocker, ["Y", "N"]);
            RequireAllowed(errors, Record40, "Equivalent e-doc", a.EquivalentEDoc, ["Y", "N"]);
            Require(errors, Record40, "Address exactly match with Deemed PoA / OVD", a.AddressExactlyMatch,
                "Address exactly match with Deemed PoA / OVD is mandatory for an OVD other than H.");
            Require(errors, Record40, "Copy of OVD", a.CopyOfOvd,
                "Copy of OVD is mandatory when the current address differs and its proof is an OVD other than H.");
        }
        if (ovd && IsOneOf(a.ProofOfAddressType, ["A", "D"]))
            Require(errors, Record40, "Driving licence / Passport Expiry Date", a.OvdExpiryDate,
                "Driving licence / Passport Expiry Date is mandatory for proof type A or D.");
        if (deemed)
        {
            RequireAllowed(errors, Record40, "Deemed POA", a.DeemedPoa, ["01", "02", "03", "04", "05"]);
            RequireAllowed(errors, Record40, "Deemed PoA Verified", a.DeemedPoaVerified, ["Y", "N"]);
            Require(errors, Record40, "Address exactly match with Deemed PoA / OVD", a.AddressExactlyMatch,
                "Address exactly match with Deemed PoA / OVD is mandatory for Deemed PoA.");
        }

        RequireAllowed(errors, Record40, "Remote Geo Tagging", a.RemoteGeoTagging, ["Y", "N"]);
        RequireAllowed(errors, Record40, "Positive verification of current address", a.PositiveVerification, ["Y", "N"]);
        RequireAllowed(errors, Record40, "Physical verification by third party", a.PhysicalVerificationByThirdParty, ["Y", "N"]);
        RequireAllowed(errors, Record40, "Physical verification by RE official", a.PhysicalVerificationByReOfficial, ["Y", "N"]);

        var foreign = !Is(r.Nationality, "IN") || Is(ResidentialStatusValue(r.ResidentialStatus), "D");
        if (foreign)
            Require(errors, Record40, "Foreign jurisdiction / embassy document", a.ForeignGovernmentDocument,
                "A foreign government or embassy document is mandatory for a non-Indian national or foreign national.");
    }

    private static List<ValidationError> ValidateRecord50(ContactDetails c)
    {
        var errors = new List<ValidationError>();

        // Country code — mandatory when a mobile number is provided.
        if (!string.IsNullOrWhiteSpace(c.MobileNumber) && string.IsNullOrWhiteSpace(c.CountryCode))
            errors.Add(Error(Record50, null, "Country code", c.CountryCode,
                "Country code is mandatory when a mobile number is provided."));

        // Mobile validated through OTP / third party — when a mobile number is provided.
        if (!string.IsNullOrWhiteSpace(c.MobileNumber) && string.IsNullOrWhiteSpace(c.MobileValidatedViaOtp))
            errors.Add(Error(Record50, null, "Mobile Number validated through OTP", c.MobileValidatedViaOtp,
                "Mobile Number validated through OTP is mandatory when a mobile number is provided."));
        if (!string.IsNullOrWhiteSpace(c.MobileNumber) && string.IsNullOrWhiteSpace(c.MobileValidatedViaThirdParty))
            errors.Add(Error(Record50, null, "Mobile Number validated through third party", c.MobileValidatedViaThirdParty,
                "Mobile Number validated through third party service provider is mandatory when a mobile number is provided."));

        // Email validated through OTP — when an email id is provided.
        if (!string.IsNullOrWhiteSpace(c.Email) && string.IsNullOrWhiteSpace(c.EmailValidatedViaOtp))
            errors.Add(Error(Record50, null, "Email validated through OTP", c.EmailValidatedViaOtp,
                "Email validated through OTP is mandatory when an email id is provided."));

        if (!string.IsNullOrWhiteSpace(c.MobileNumber))
        {
            if (!DigitsPattern.IsMatch(c.MobileNumber) || c.MobileNumber.Length is < 8 or > 15)
                errors.Add(Error(Record50, null, "Mobile number", c.MobileNumber,
                    "Mobile number must contain 8 to 15 digits."));
            if (Is(c.CountryCode, "+91") && c.MobileNumber.Length != 10)
                errors.Add(Error(Record50, null, "Mobile number", c.MobileNumber,
                    "An Indian mobile number must contain exactly 10 digits."));
        }

        return errors;
    }

    private static List<ValidationError> ValidateRecord70(Individual r, OtherDetails o)
    {
        var errors = new List<ValidationError>();

        RequireAllowed(errors, Record70, "Video KYC without official", o.VideoKycWithoutOfficial, ["Y", "N"]);
        RequireAllowed(errors, Record70, "Video KYC with RE official", o.VideoKycWithReOfficial, ["Y", "N"]);
        RequireAllowed(errors, Record70, "Face to Face with RE official", o.FaceToFaceWithReOfficial, ["Y", "N"]);
        RequireAllowed(errors, Record70, "Face to Face with non-official", o.FaceToFaceWithNonOfficial, ["Y", "N"]);

        var selectedModes = new[] { o.VideoKycWithoutOfficial, o.VideoKycWithReOfficial,
            o.FaceToFaceWithReOfficial, o.NonFaceToFace, o.FaceToFaceWithNonOfficial }.Count(v => Is(v, "Y"));
        if (selectedModes != 1)
            errors.Add(Error(Record70, null, "Mode of KYC", null, "Exactly one Mode of KYC must be selected."));

        // Non face to face — CM, mandatory when KYC type is O.
        if (Is(r.KycType, "O") && string.IsNullOrWhiteSpace(o.NonFaceToFace))
            errors.Add(Error(Record70, null, "Non face to face", o.NonFaceToFace,
                "Non face to face is mandatory when KYC type is O."));

        // Attestation employee fields — CM, mandatory when Mode of KYC = Face to Face with RE official.
        if (Is(o.FaceToFaceWithReOfficial, "Y"))
        {
            if (string.IsNullOrWhiteSpace(o.EmployeeName))
                errors.Add(Error(Record70, null, "Employee Name", o.EmployeeName, "Employee Name is mandatory for Face to Face with RE official."));
            if (string.IsNullOrWhiteSpace(o.EmployeeCode))
                errors.Add(Error(Record70, null, "Employee Code", o.EmployeeCode, "Employee Code is mandatory for Face to Face with RE official."));
            if (string.IsNullOrWhiteSpace(o.EmployeeDesignation))
                errors.Add(Error(Record70, null, "Employee Designation", o.EmployeeDesignation, "Employee Designation is mandatory for Face to Face with RE official."));
            if (string.IsNullOrWhiteSpace(o.EmployeeBranch))
                errors.Add(Error(Record70, null, "Employee Branch", o.EmployeeBranch, "Employee Branch is mandatory for Face to Face with RE official."));
            if (string.IsNullOrWhiteSpace(o.EmployeeCkycId))
                errors.Add(Error(Record70, null, "Employee CKYC ID", o.EmployeeCkycId, "Employee CKYC ID is mandatory for Face to Face with RE official."));
        }

        // Mandatory attestation fields.
        if (string.IsNullOrWhiteSpace(o.AttestationDate))
            errors.Add(Error(Record70, null, "Attestation Date", o.AttestationDate, "Attestation Date is mandatory."));
        if (string.IsNullOrWhiteSpace(o.InstitutionName))
            errors.Add(Error(Record70, null, "Institution Name", o.InstitutionName, "Institution Name is mandatory."));
        if (string.IsNullOrWhiteSpace(o.InstitutionCode))
            errors.Add(Error(Record70, null, "Institution Code", o.InstitutionCode, "Institution Code is mandatory."));
        if (string.IsNullOrWhiteSpace(o.DeclarationDocument))
            errors.Add(Error(Record70, null, "Declaration Document", o.DeclarationDocument, "Declaration Document is mandatory."));
        RequireAllowed(errors, Record70, "Declaration Flag", o.DeclarationFlag, ["Y", "N"]);
        if (string.IsNullOrWhiteSpace(o.ClientConsent))
            errors.Add(Error(Record70, null, "Client Consent", o.ClientConsent, "Client Consent is mandatory."));
        if (string.IsNullOrWhiteSpace(o.Place))
            errors.Add(Error(Record70, null, "Place", o.Place, "Place is mandatory."));
        if (string.IsNullOrWhiteSpace(o.DeclarationDate))
            errors.Add(Error(Record70, null, "Declaration Date", o.DeclarationDate, "Declaration Date is mandatory."));
        else if (!IsDate(o.DeclarationDate))
            errors.Add(Error(Record70, null, "Declaration Date", o.DeclarationDate,
                "Declaration Date must use DD-MM-YYYY format and be a valid date."));

        if (!string.IsNullOrWhiteSpace(o.AttestationDate) && !IsDate(o.AttestationDate))
            errors.Add(Error(Record70, null, "Attestation Date", o.AttestationDate,
                "Attestation Date must use DD-MM-YYYY format and be a valid date."));

        return errors;
    }

    private static void Require(List<ValidationError> errors, string recordType, string field, string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(Error(recordType, null, field, value, description));
    }

    private static void OptionalAllowed(List<ValidationError> errors, string recordType, string field, string? value, string[] allowed)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsOneOf(value, allowed))
            errors.Add(Error(recordType, null, field, value, $"{field} must be one of: {string.Join(", ", allowed)}."));
    }

    private static void ValidateRelatedName(PersonName name, string section, List<ValidationError> errors)
    {
        if (!name.HasAnyName) return;
        RequireAllowed(errors, Record20, $"{section} Title", name.Title,
            ["Mr", "Mr.", "Ms", "Ms.", "Mrs", "Mrs.", "Mx", "Mx."]);
        Require(errors, Record20, $"{section} First Name", name.FirstName,
            $"{section} First Name is mandatory when any part of {section} is supplied.");
    }

    private static void RequireAllowed(List<ValidationError> errors, string recordType, string field, string? value, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(Error(recordType, null, field, value, $"{field} is mandatory."));
        else if (!IsOneOf(value, allowed))
            errors.Add(Error(recordType, null, field, value, $"{field} must be one of: {string.Join(", ", allowed)}."));
    }

    private static bool IsOneOf(string? value, string[] allowed) => allowed.Any(v => Is(value, v));

    private static bool IsDate(string? value) =>
        DateOnly.TryParseExact(value, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsCompactDate(string? value) =>
        DateOnly.TryParseExact(value, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsBelowTen(string? dateOfBirth) =>
        IsDate(dateOfBirth) && DateOnly.ParseExact(dateOfBirth!, "dd-MM-yyyy", CultureInfo.InvariantCulture)
            .AddYears(10) > DateOnly.FromDateTime(DateTime.Today);

    private static string? ResidentialStatusValue(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "RESIDENT" => "A",
        "NRI" => "B",
        "PIO" => "C",
        "FOREIGNNATIONAL" or "FOREIGN NATIONAL" => "D",
        _ => value,
    };

    private static bool SameAddress(AddressDetails? a, AddressDetails? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return Is(a.Line1, b.Line1) && Is(a.Line2, b.Line2) && Is(a.Line3, b.Line3)
            && Is(a.Country, b.Country) && Is(a.State, b.State) && Is(a.District, b.District)
            && Is(a.City, b.City) && Is(a.PinCode, b.PinCode);
    }

    private static ValidationError Error(string recordType, string? line, string field, string? value, string description)
        => new(null, recordType, line, field, value, null, description);
}
