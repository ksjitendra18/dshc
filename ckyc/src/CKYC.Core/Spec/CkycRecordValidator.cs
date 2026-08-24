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

    private static bool Is(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns every rule violation for the record, or an empty list when it is valid.</summary>
    public static IReadOnlyList<ValidationError> Validate(Individual r)
    {
        var errors = new List<ValidationError>();

        ValidateRecord20(r, errors);

        foreach (var p in r.Proofs)
            errors.AddRange(ValidateRecord30(r, p));

        if (r.Proofs.Count == 0)
            errors.Add(Error(Record30, null, "OVD Details", null, "At least one record type 30 (Proof of Identity & Address) is required."));

        errors.AddRange(ValidateRecord40(r));

        var hasContact = r.Contact is not null;
        if (hasContact) errors.AddRange(ValidateRecord50(r.Contact!));
        else errors.Add(Error(Record50, null, "Contact Details", null, "Record type 50 (Contact Details) is required."));

        errors.AddRange(EachRelatedParty(r));

        if (r.Other is null)
            errors.Add(Error(Record70, null, "Other Details & Attestation", null, "Record type 70 (Other Details & Attestation) is required."));
        else
            errors.AddRange(ValidateRecord70(r, r.Other));

        return errors;
    }

    private static IEnumerable<ValidationError> EachRelatedParty(Individual r)
    {
        if (r.RelatedParties.Count == 0)
            yield return Error(Record60, null, "Related Party Details", null, "Record type 60 (Related Party) is required.");

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
        // Name (record type 20) — mandatory
        if (string.IsNullOrWhiteSpace(r.Name.FirstName))
            errors.Add(Error(Record20, null, "First Name", r.Name.FirstName, "Name (First Name) is mandatory."));

        if (string.IsNullOrWhiteSpace(r.DateOfBirth))
            errors.Add(Error(Record20, null, "Date of Birth", r.DateOfBirth, "Date of Birth is mandatory."));

        // At least one of Mother / Father / Spouse name (conditional-mandatory).
        if (!r.MotherName.HasAnyName && !r.FatherName.HasAnyName && !r.SpouseName.HasAnyName)
            errors.Add(Error(Record20, null, "Mother / Father / Spouse Name", null,
                "At least one of Mother Name, Father Name or Spouse Name must be provided."));

        // Gender matching with OVD — mandatory when "Gender provided in OVD" = Y.
        if (Is(r.GenderProvidedInOvd, "Y") && string.IsNullOrWhiteSpace(r.GenderMatchWithOvd))
            errors.Add(Error(Record20, null, "Gender matching with OVD", r.GenderMatchWithOvd,
                "Gender matching with OVD is mandatory when Gender provided in OVD is Y."));

        // One of PAN / Form 97 (erstwhile Form 60) / Form 61 is required.
        var panOrForm = !string.IsNullOrWhiteSpace(r.Pan) || Is(r.Form97Provided, "Y") || Is(r.Form61Provided, "Y");
        if (!panOrForm)
            errors.Add(Error(Record20, null, "PAN / Form 97 / Form 61", r.Pan,
                "Any one from PAN, Form 97 (erstwhile Form 60) or Form 61 is required."));

        // PAN verified — mandatory where PAN is provided.
        if (!string.IsNullOrWhiteSpace(r.Pan) && string.IsNullOrWhiteSpace(r.PanVerified))
            errors.Add(Error(Record20, null, "PAN verified", r.PanVerified, "PAN verified is mandatory when PAN is provided."));

        // PAN supporting document — mandatory when PAN is provided.
        if (!string.IsNullOrWhiteSpace(r.Pan) && string.IsNullOrWhiteSpace(r.PanDocument))
            errors.Add(Error(Record20, null, "PAN Document", r.PanDocument, "PAN supporting document is mandatory when PAN is provided."));

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

        // Mode of Aadhaar Verification — mandatory for KYC type "O".
        if (ovd == "E" && kycO && string.IsNullOrWhiteSpace(p.ModeOfAadhaarVerification))
            errors.Add(Error(Record30, null, "Mode of Aadhaar Verification", p.ModeOfAadhaarVerification,
                "Mode of Aadhaar Verification (eKYC Authentication) is mandatory for KYC type O."));

        // Passport expiry date — required when Passport (A) is the OVD.
        if (ovd == "A" && string.IsNullOrWhiteSpace(p.PassportExpiryDate))
            errors.Add(Error(Record30, null, "Passport expiry date", p.PassportExpiryDate,
                "Passport expiry date is required when the OVD is a Passport."));

        // Driving licence expiry date — required when Driving Licence (D) is the OVD.
        if (ovd == "D" && string.IsNullOrWhiteSpace(p.DrivingLicenseExpiryDate))
            errors.Add(Error(Record30, null, "Driving licence expiry date", p.DrivingLicenseExpiryDate,
                "Driving licence expiry date is required when the OVD is a Driving Licence."));

        // Length of Aadhaar/VID — applicable for OVD type E only.
        if (ovd == "E" && string.IsNullOrWhiteSpace(p.LengthOfAadhaar))
            errors.Add(Error(Record30, null, "Length of Aadhaar/VID", p.LengthOfAadhaar,
                "Length of Aadhaar/VID is required for OVD type E."));

        // ID Number — applicable for all OVD types except "H".
        if (ovd != "H" && string.IsNullOrWhiteSpace(p.IdNumber))
            errors.Add(Error(Record30, null, "ID Number", p.IdNumber, "ID Number is required for all OVD types except ID not available (H)."));

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

        // Mode of Authentication — applicable for Aadhaar OVD in E-KYC mode.
        if (ovd == "E" && Is(p.ModeOfAadhaarVerification, "B") && string.IsNullOrWhiteSpace(p.ModeOfAuthentication))
            errors.Add(Error(Record30, null, "Mode of Authentication", p.ModeOfAuthentication,
                "Mode of Authentication is required for Aadhaar OVD in E-KYC mode."));

        // E-KYC data received from UIDAI — mandatory for KYC type O / Aadhaar E-KYC.
        if (ovd == "E" && (kycO || Is(p.ModeOfAadhaarVerification, "B")) && string.IsNullOrWhiteSpace(p.EkycDataFromUidai))
            errors.Add(Error(Record30, null, "E-KYC data received from UIDAI", p.EkycDataFromUidai,
                "E-KYC data received from UIDAI is mandatory for KYC type O or Aadhaar E-KYC."));

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
            ValidateAddressBlock(r.PermanentAddress, "Permanent Address", Record40, errors);
        }

        var current = r.CurrentAddress;
        if (current is null)
        {
            errors.Add(Error(Record40, null, "Current Address", null, "Current Address is required (a record type 40 is expected)."));
        }
        else
        {
            // Current-address CM fields are applicable when the current address differs from permanent.
            ValidateAddressBlock(current, "Current Address", Record40, errors);

            // Proof-of-address sub-fields (Proof of Address type, Deemed POA, etc.) are derived by the
            // writer from the record-30 OVD when absent, so they are not treated as hard validation errors.
        }

        return errors;
    }

    private static void ValidateAddressBlock(AddressDetails a, string section, string recordType, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(a.Line1))
            errors.Add(Error(recordType, null, $"{section} Line 1", a.Line1,
                $"{section} (Flat No / House No) is mandatory."));

        // Country-aware CM fields: State/District/City/PinCode become mandatory when Country = IN.
        var isIndia = string.IsNullOrWhiteSpace(a.Country) || Is(a.Country, "IN");
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
        if (string.IsNullOrWhiteSpace(a.AddressSupportedWithDocument))
            errors.Add(Error(recordType, null, $"{section} address supported with document", a.AddressSupportedWithDocument,
                $"{section} address supported with document is mandatory."));
        if (string.IsNullOrWhiteSpace(a.AddressMatchWithOvd))
            errors.Add(Error(recordType, null, $"{section} Address match with OVD", a.AddressMatchWithOvd,
                $"{section} Address match with OVD is mandatory."));
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

        return errors;
    }

    private static List<ValidationError> ValidateRecord70(Individual r, OtherDetails o)
    {
        var errors = new List<ValidationError>();

        // Mode of KYC — at least one mode must be selected.
        var anyMode = Is(o.VideoKycWithoutOfficial, "Y") || Is(o.VideoKycWithReOfficial, "Y")
            || Is(o.FaceToFaceWithReOfficial, "Y") || Is(o.NonFaceToFace, "Y") || Is(o.FaceToFaceWithNonOfficial, "Y");
        if (!anyMode)
            errors.Add(Error(Record70, null, "Mode of KYC", null, "At least one Mode of KYC must be selected."));

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
        if (string.IsNullOrWhiteSpace(o.ClientConsent))
            errors.Add(Error(Record70, null, "Client Consent", o.ClientConsent, "Client Consent is mandatory."));
        if (string.IsNullOrWhiteSpace(o.Place))
            errors.Add(Error(Record70, null, "Place", o.Place, "Place is mandatory."));
        if (string.IsNullOrWhiteSpace(o.DeclarationDate))
            errors.Add(Error(Record70, null, "Declaration Date", o.DeclarationDate, "Declaration Date is mandatory."));

        return errors;
    }

    private static ValidationError Error(string recordType, string? line, string field, string? value, string description)
        => new(null, recordType, line, field, value, null, description);
}
