namespace CKYC.Core.Spec;

/// <summary>
/// Field layouts for the CERSAI <b>bulk update</b> (.UPD) file formats, transcribed from
/// the vendor workbooks:
///   • individual — <c>vendor/individual-format-update.xlsx</c> (sheets 10/20/30/40/50/60/70)
///   • legal entity — <c>vendor/legal-format-update.xlsx</c> (same sheet names)
///
/// Every detail line opens with Record Type ("20".."70"), Line Number and the existing
/// CKYC Number, then carries per-section "<c>*Update Flg</c>" switches; fields governed by
/// a flag are conditional-mandatory (<see cref="FieldRequirement.CM"/>) only when that flag
/// is "Y", otherwise they must stay blank. Each line ends with the FVU-managed Hash Value
/// placeholder column, matching the trailing pipe in the vendor samples.
/// </summary>
public static class UpdateFormat
{
    /// <summary>M / O / CM as printed in the "M/O/CM" columns of the sheets.</summary>
    public enum FieldRequirement { M, O, CM }

    /// <summary>A single .UPD field position.</summary>
    /// <param name="Key">Stable lookup key used by JSON intake, writers and validators.</param>
    /// <param name="Title">The sheet's Description cell.</param>
    /// <param name="Size">Maximum value length (0 = unlimited).</param>
    /// <param name="Requirement">M/O/CM.</param>
    /// <param name="Flag">True when this position is itself a section "*Update Flg" switch.</param>
    /// <param name="Document">True when the value is a support_docs file name (&lt;500 KB PDF/JPG/JPEG).</param>
    /// <param name="Date">DD-MM-YYYY calendar value (normalised by the loader).</param>
    /// <param name="CompactDate">DDMMYYYY calendar value (legal entity dates of incorporation).</param>
    public sealed record Field(
        string Key, string Title, int Size, FieldRequirement Requirement,
        bool Flag = false, bool Document = false, bool Date = false, bool CompactDate = false);

    public sealed record Layout(string ClientType, string RecordType, string Section, IReadOnlyList<Field> Fields);

    // ------------------------------------------------------------------
    // Header record (10). The FVU appends its own version number plus the
    // record/file-level hashes, so we author only through the last filler.
    // ------------------------------------------------------------------
    private static readonly Field[] IndividualHeaderFields =
    {
        new("recordType", "Record Type", 2, FieldRequirement.M),
        new("fiCode", "FI Code", 6, FieldRequirement.M),
        new("regionCode", "Region Code / Branch Code", 11, FieldRequirement.CM),
        new("clientType", "Client Type", 1, FieldRequirement.M),
        new("totalDetailRecords", "Total No of Detail Records", 8, FieldRequirement.M),
        new("versionNumber", "Version Number", 6, FieldRequirement.M),
        new("createDate", "Create Date", 10, FieldRequirement.M),
        new("filler1", "Filler 1", 50, FieldRequirement.O),
        new("filler2", "Filler 2", 50, FieldRequirement.O),
    };

    private static readonly Field[] LegalHeaderFields =
    {
        IndividualHeaderFields[0], IndividualHeaderFields[1], IndividualHeaderFields[2],
        IndividualHeaderFields[3], IndividualHeaderFields[4], IndividualHeaderFields[5], IndividualHeaderFields[6],
        IndividualHeaderFields[7], IndividualHeaderFields[8],
        new("filler3", "Filler 3", 50, FieldRequirement.O),   // legal header carries an extra filler
    };

    // ------------------------------------------------------------------
    // Individual record 20 — Demographic Details (sheet '20', rows 3-186).
    // ------------------------------------------------------------------
    private static readonly Field[] Individual20 =
    {
        new("kycTypeFlg", "KYC Type Update Flg", 1, FieldRequirement.O, Flag: true),
        new("kycType", "KYC Type", 1, FieldRequirement.CM),                                    // N/M/S/O
        new("nameFlg", "Name details Update Flg", 1, FieldRequirement.O, Flag: true),
        new("nameTitle", "Title", 4, FieldRequirement.CM),                                     // Mr/Ms/Mrs/Mx
        new("firstName", "First Name", 33, FieldRequirement.CM),
        new("middleName", "Middle Name", 33, FieldRequirement.O),
        new("lastName", "Last Name", 33, FieldRequirement.O),
        new("maidenNameFlg", "Maiden Name Update Flg", 1, FieldRequirement.O, Flag: true),
        new("maidenTitle", "Maiden Name Title", 4, FieldRequirement.CM),
        new("maidenFirstName", "Maiden First Name", 33, FieldRequirement.CM),
        new("maidenMiddleName", "Maiden Middle Name", 33, FieldRequirement.O),
        new("maidenLastName", "Maiden Last Name", 33, FieldRequirement.O),
        new("motherNameFlg", "Mother Name Update Flg", 1, FieldRequirement.O, Flag: true),
        new("motherTitle", "Mother Title", 4, FieldRequirement.CM),
        new("motherFirstName", "Mother First Name", 33, FieldRequirement.CM),
        new("motherMiddleName", "Mother Middle Name", 33, FieldRequirement.O),
        new("motherLastName", "Mother Last Name", 33, FieldRequirement.O),
        new("fatherNameFlg", "Father Name Update Flg", 1, FieldRequirement.O, Flag: true),
        new("fatherTitle", "Father Title", 4, FieldRequirement.CM),
        new("fatherFirstName", "Father First Name", 33, FieldRequirement.CM),
        new("fatherMiddleName", "Father Middle Name", 33, FieldRequirement.O),
        new("fatherLastName", "Father Last Name", 33, FieldRequirement.O),
        new("spouseNameFlg", "Spouse Name Update Flg", 1, FieldRequirement.O, Flag: true),
        new("spouseTitle", "Spouse Title", 4, FieldRequirement.CM),
        new("spouseFirstName", "Spouse First Name", 33, FieldRequirement.CM),
        new("spouseMiddleName", "Spouse Middle Name", 33, FieldRequirement.O),
        new("spouseLastName", "Spouse Last Name", 33, FieldRequirement.O),
        new("dobFlg", "Date of birth Update Flg", 1, FieldRequirement.O, Flag: true),
        new("dateOfBirth", "Date of birth", 10, FieldRequirement.CM, Date: true),              // DD-MM-YYYY
        new("minor", "Minor", 1, FieldRequirement.CM),                                         // Y/N
        new("dobMatchWithOvd", "DOB matching with OVD", 1, FieldRequirement.CM),               // Y/N
        new("nameMatchWithOvd", "Name matching with OVD", 1, FieldRequirement.CM),             // Y/N
        new("photoMatchWithOvd", "Photo provided matching with photo on the OVD", 1, FieldRequirement.CM),
        new("gender", "Gender", 1, FieldRequirement.CM),                                       // M/F/T
        new("genderProvidedInOvd", "Gender provided in OVD", 1, FieldRequirement.CM),          // Y/N
        new("genderMatchWithOvd", "Gender matching with OVD", 1, FieldRequirement.CM),         // Y/N when provided=Y
        new("panSectionFlg", "PAN/Form 97/61 Update flg", 1, FieldRequirement.O, Flag: true),
        new("pan", "PAN", 10, FieldRequirement.CM),
        new("form97Provided", "Form 97 (erstwhile form 60)", 1, FieldRequirement.CM),
        new("form61Provided", "Form 61", 1, FieldRequirement.CM),
        new("panVerified", "PAN verified", 1, FieldRequirement.CM),                            // Y/N, PAN provided
        new("residentialStatusFlg", "Residential Status Update flg", 1, FieldRequirement.O, Flag: true),
        new("residentialStatus", "Residential Status", 1, FieldRequirement.CM),                // A/B/C/D
        new("residentialStatusSupportedByDoc", "Residential Status supported with document", 1, FieldRequirement.CM),
        new("nationalityFlg", "Nationality Update Flg", 1, FieldRequirement.O, Flag: true),
        new("nationality", "Nationality", 2, FieldRequirement.O),
        new("nationalitySupportedByDoc", "Nationality supported with document", 1, FieldRequirement.CM),
        new("disabilityFlg", "Person with Disability (PwD) status Update Flg", 1, FieldRequirement.O, Flag: true),
        new("differentlyAbled", "Person with Disability (PwD)", 1, FieldRequirement.CM),       // Y/N
        new("impairmentType", "Type of Impairment", 2, FieldRequirement.CM),
        new("otherImpairmentType", "Other Type of Impairment", 150, FieldRequirement.CM),
        new("disabilityReferenceNumber", "Certificate of Disability reference number / UDID Card Number", 18, FieldRequirement.CM),
        new("permanentDisability", "Permanent disability", 1, FieldRequirement.CM),
        new("disabilityDate", "Disability date", 10, FieldRequirement.CM, Date: true),
        new("percentageOfImpairment", "Percentage of Impairment", 3, FieldRequirement.CM),
        new("disabilitySupportedByDoc", "Differently Abled status Supported by document", 1, FieldRequirement.CM),
        new("documentFlg", "Document Update Flg", 1, FieldRequirement.O, Flag: true),
        new("panDocument", "PAN document", 125, FieldRequirement.O, Document: true),
        new("photoOfIndividual", "Photo of Individual", 125, FieldRequirement.CM, Document: true),
        new("countRecord30", "Count of Record Type 30", 5, FieldRequirement.O),   // computed at write time
        new("countRecord40", "Count of Record Type 40", 5, FieldRequirement.O),
        new("countRecord50", "Count of Record Type 50", 5, FieldRequirement.O),
        new("countRecord60", "Count of Record Type 60", 5, FieldRequirement.O),
        new("countRecord70", "Count of Record Type 70", 5, FieldRequirement.O),
    };

    // ------------------------------------------------------------------
    // Individual record 30 — Proof of Identity and Address (sheet '30').
    // ------------------------------------------------------------------
    private static readonly Field[] Individual30 =
    {
        new("kycType", "KYC Type", 1, FieldRequirement.CM),
        new("ovdDetailsFlg", "OVD Details Update Flg", 1, FieldRequirement.O, Flag: true),
        new("ovdType", "OVD Type", 1, FieldRequirement.CM),                       // A/B/D/E/F/G/H
        new("modeOfAadhaarVerification", "Mode of Aadhaar Verification", 1, FieldRequirement.CM),  // A/B/C
        new("passportExpiryDate", "Passport expiry date", 10, FieldRequirement.CM, Date: true),
        new("drivingLicenseExpiryDate", "Driving license expiry date", 10, FieldRequirement.CM, Date: true),
        new("lengthOfAadhaar", "Length of Aadhaar/VID", 1, FieldRequirement.CM),  // A = four digit masked
        new("idNumber", "ID Number", 100, FieldRequirement.CM),
        new("certifiedCopyWithOriginal", "Certified copy verified with original OVD", 1, FieldRequirement.CM),
        new("equivalentEDoc", "Equivalent e-doc", 1, FieldRequirement.CM),
        new("verifiedFromDigiLocker", "Document verified from digilocker", 1, FieldRequirement.CM),
        new("presenceInMeaRepository", "Presence of Passport in MEA repository", 1, FieldRequirement.CM),
        new("presenceInEciRepository", "Presence of Voter ID in ECI repository", 1, FieldRequirement.CM),
        new("presenceInRtoRepository", "Presence of Driving License in RTO repository", 1, FieldRequirement.CM),
        new("presenceInNregaRepository", "Presence of NREGA in respective repository", 1, FieldRequirement.CM),
        new("presenceInNprRecords", "Presence of NPR in census records / respective repository", 1, FieldRequirement.CM),
        new("dataFromOfflineVerification", "Data received from offline verification", 1, FieldRequirement.CM),
        new("modeOfAuthentication", "Mode of Authentication", 1, FieldRequirement.CM),            // A/B/C
        new("ekycDataFromUidai", "E-KYC data received from UIDAI", 1, FieldRequirement.CM),
        new("documentsFlg", "Document update Flg", 1, FieldRequirement.O, Flag: true),
        new("copyOfOvd", "Copy of OVD/POI", 125, FieldRequirement.CM, Document: true),
    };

    // ------------------------------------------------------------------
    // Individual record 40 — Address Details, permanent + current (sheet '40').
    // ------------------------------------------------------------------
    private static readonly Field[] Individual40 =
    {
        new("kycType", "KYC Type", 1, FieldRequirement.CM),
        new("permanentAddressFlg", "Permanent Address update Flg", 1, FieldRequirement.O, Flag: true),
        new("permLine1", "Flat No / House No (Address Line 1)", 60, FieldRequirement.CM),
        new("permLine2", "Plot No / Apartment Name (Address Line 2)", 60, FieldRequirement.O),
        new("permLine3", "Locality / Street (Address Line 3)", 60, FieldRequirement.O),
        new("permCountry", "Country", 2, FieldRequirement.CM),
        new("permState", "State / UT", 2, FieldRequirement.CM),
        new("permDistrict", "District", 6, FieldRequirement.CM),
        new("permCity", "City/town/village", 60, FieldRequirement.CM),
        new("permPinCode", "Pin Code", 6, FieldRequirement.CM),
        new("permPinOthers", "Pin code (in case of others)", 6, FieldRequirement.CM),
        new("permDigipin", "Digipin", 10, FieldRequirement.O),
        new("permSupportedWithDocument", "Address (supported with document)", 1, FieldRequirement.CM),
        new("permMatchWithOvd", "Address match with OVD", 13, FieldRequirement.CM),               // Exact/Partial/No
        new("currentAddressFlg", "Current Address Update flg", 1, FieldRequirement.O, Flag: true),
        new("currentSameAsPermanent", "Same as permanent address", 1, FieldRequirement.CM),
        new("currLine1", "Current Address Line 1", 60, FieldRequirement.CM),
        new("currLine2", "Current Address Line 2", 60, FieldRequirement.O),
        new("currLine3", "Current Address Line 3", 60, FieldRequirement.O),
        new("currCountry", "Current Country", 2, FieldRequirement.CM),
        new("currState", "Current State / UT", 2, FieldRequirement.CM),
        new("currDistrict", "Current District", 6, FieldRequirement.CM),
        new("currCity", "Current City/town/village", 60, FieldRequirement.CM),
        new("currPinCode", "Current Pin Code", 6, FieldRequirement.CM),
        new("currPinOthers", "Current Pin code (in case of others)", 6, FieldRequirement.CM),
        new("currDigipin", "Current Digipin", 10, FieldRequirement.O),
        new("currProofOfAddress", "Proof of Address", 1, FieldRequirement.CM),                    // 1/2/3
        new("currProofOfAddressType", "Proof of Address Type", 1, FieldRequirement.CM),
        new("currLengthOfAadhaar", "Length of Aadhaar/VID", 1, FieldRequirement.CM),
        new("currIdNumber", "ID Number", 20, FieldRequirement.CM),
        new("currModeOfAadhaarVerification", "Mode of Aadhaar Verification", 1, FieldRequirement.CM),
        new("currOvdExpiryDate", "Driving license/Passport Expiry Date", 20, FieldRequirement.CM, Date: true),
        new("currDeemedPoa", "Deemed POA", 2, FieldRequirement.CM),                               // 01..05
        new("currDeemedPoaVerified", "Deemed PoA Verified", 1, FieldRequirement.CM),
        new("currCertifiedCopyWithOriginal", "Certified copy verified with original OVD", 1, FieldRequirement.CM),
        new("currVerifiedFromDigiLocker", "Document verified from Digilocker", 1, FieldRequirement.CM),
        new("currEquivalentEDoc", "Equivalent e-doc", 1, FieldRequirement.CM),
        new("currRemoteGeoTagging", "Remote Geo Tagging", 1, FieldRequirement.CM),
        new("currAddressExactlyMatch", "Address exactly match with Deemed PoA/Deemed OVD", 1, FieldRequirement.CM),
        new("currPositiveVerification", "Positive verification of current address through letter or deliveries", 1, FieldRequirement.CM),
        new("currPhysicalVerificationThirdParty", "Physical verification (including geo tagging) by third party", 1, FieldRequirement.CM),
        new("currPhysicalVerificationReOfficial", "Physical verification (including geo tagging) by RE official", 1, FieldRequirement.CM),
        new("currPresenceInRepository", "Presence of driving license/Passport/Voter ID/NREGA/NPR in census records or respective repository", 1, FieldRequirement.CM),
        new("documentsFlg", "Doucument update flg", 1, FieldRequirement.O, Flag: true),
        new("foreignJurisdictionDocument", "Any other documents issued by Government departments of foreign jurisdictions / Foreign Embassy or Mission letter", 125, FieldRequirement.CM, Document: true),
        new("copyOfOvd", "Copy of OVD", 125, FieldRequirement.CM, Document: true),
    };

    // ------------------------------------------------------------------
    // Individual record 50 — Contact Details (sheet '50').
    // ------------------------------------------------------------------
    private static readonly Field[] Individual50 =
    {
        new("contactDetailsFlg", "Contact Details Flg", 1, FieldRequirement.O, Flag: true),
        new("email", "Email Address", 254, FieldRequirement.O),
        new("countryCode", "Country code", 4, FieldRequirement.CM),
        new("mobileNumber", "Mobile number", 15, FieldRequirement.O),
        new("mobileValidatedViaOtp", "Mobile Number validated through OTP", 1, FieldRequirement.CM),
        new("emailValidatedViaOtp", "Email validated through OTP", 1, FieldRequirement.CM),
        new("mobileValidatedViaThirdParty", "Mobile Number validated through third party service provider", 1, FieldRequirement.CM),
    };

    // ------------------------------------------------------------------
    // Individual record 60 — Related Party Details (sheet '60').
    // ------------------------------------------------------------------
    private static readonly Field[] Individual60 =
    {
        new("relatedPartyFlg", "Related Party Details Flg", 1, FieldRequirement.O, Flag: true),
        new("relatedPersonType", "Related Person Type", 60, FieldRequirement.CM),                 // A/B/C
        new("ckycNumberOfRelatedPerson", "CKYC Number of Related Person", 14, FieldRequirement.CM),
    };

    // ------------------------------------------------------------------
    // Individual record 70 — Other Details and Attestation (sheet '70').
    // ------------------------------------------------------------------
    private static readonly Field[] Individual70 =
    {
        new("kycType", "KYC Type", 1, FieldRequirement.CM),
        new("otherDetailsFlg", "Other details Flg", 1, FieldRequirement.O, Flag: true),
        new("remarks", "Remarks", 200, FieldRequirement.O),
        new("modeOfKycFlg", "Mode of KYC Flg", 1, FieldRequirement.O, Flag: true),
        new("videoKycWithoutOfficial", "Video KYC without official (i.e. automated video KYC)", 1, FieldRequirement.CM),
        new("videoKycWithReOfficial", "Video KYC with RE official", 1, FieldRequirement.CM),
        new("faceToFaceWithReOfficial", "Face to Face with RE official", 1, FieldRequirement.CM),
        new("nonFaceToFace", "Non face to face", 1, FieldRequirement.CM),
        new("faceToFaceWithNonOfficial", "Face to Face with non-official such as Business Correspondent", 1, FieldRequirement.CM),
        new("attestationFlg", "Attestation Details flg", 1, FieldRequirement.O, Flag: true),
        new("attestationDate", "Attestation Date", 10, FieldRequirement.CM, Date: true),
        new("employeeName", "Employee Name", 50, FieldRequirement.CM),
        new("employeeCode", "Employee Code", 50, FieldRequirement.CM),
        new("employeeDesignation", "Employee Designation", 50, FieldRequirement.CM),
        new("employeeBranch", "Employee Branch", 50, FieldRequirement.CM),
        new("employeeCkycId", "Employee CKYC ID", 14, FieldRequirement.CM),
        new("institutionName", "Institution Name", 50, FieldRequirement.CM),
        new("institutionCode", "Institution Code", 6, FieldRequirement.CM),
        new("declarationDocument", "Declaration Document", 125, FieldRequirement.O, Document: true),
        new("declarationFlag", "Declaration Flag", 1, FieldRequirement.O),
        new("consentDocument", "Consent Document", 125, FieldRequirement.O, Document: true),
        new("place", "Place", 40, FieldRequirement.O),
        new("declarationDate", "Date", 10, FieldRequirement.O, Date: true),
    };

    // ------------------------------------------------------------------
    // Legal Entity record 20 — Entity Details (legal sheet '20').
    // Dates use the compact DDMMYYYY calendar format in this workbook.
    // ------------------------------------------------------------------
    private static readonly Field[] Legal20 =
    {
        new("entityDetailsFlg", "Entity Details update Flg", 1, FieldRequirement.O, Flag: true),
        new("name", "Name", 99, FieldRequirement.CM),
        new("entityConstitution", "Entity Constitution", 2, FieldRequirement.CM),                  // A..R
        new("listedCompany", "Listed Company", 1, FieldRequirement.CM),                            // Y/N, public ltd
        new("registeredFirm", "Registered Firm", 1, FieldRequirement.CM),                          // Y/N, partnership firm
        new("registeredTrust", "Registered Trust", 1, FieldRequirement.CM),                        // Y/N, trust
        new("dateOfIncorporation", "Date of incorporation/Registration/Formation", 8, FieldRequirement.CM, CompactDate: true),
        new("dateOfCommencement", "Date of commencement of business", 8, FieldRequirement.CM, CompactDate: true),
        new("placeOfIncorporation", "Place of incorporation/Registration/Formation", 50, FieldRequirement.CM),
        new("countryOfIncorporation", "Country of incorporation / Registration", 2, FieldRequirement.CM),
        new("tinIssuingCountry", "TIN or equivalent issuing country", 2, FieldRequirement.O),
        new("pan", "PAN", 10, FieldRequirement.CM),
        new("form97Provided", "Form 97 (erstwhile form 60)", 1, FieldRequirement.CM),
        new("tinGstNumber", "TIN/GST registration number", 15, FieldRequirement.O),
        new("panDocument", "PAN document", 125, FieldRequirement.CM, Document: true),
        new("panVerified", "PAN Verified", 1, FieldRequirement.CM),
        new("tinGstnDocument", "TIN/GSTN document", 125, FieldRequirement.CM, Document: true),
    };

    // ------------------------------------------------------------------
    // Legal Entity record 30 — Proof of Identity and Address is constitution-
    // specific (five blocks on sheet '30'). Exactly one block applies; the
    // writer emits RT/LN/CKYC/update-flag/constitution + that block (+ Hash).
    // ------------------------------------------------------------------
    private static readonly Field[] Legal30Company =
    {
        new("poiDetailsFlg", "POI details update Flg", 1, FieldRequirement.O, Flag: true),
        new("entityConstitution", "Entity Constitution", 2, FieldRequirement.CM),
        new("certificateOfIncorporation", "Certificate of incorporation", 125, FieldRequirement.CM, Document: true),
        new("cin", "CIN", 21, FieldRequirement.CM),
        new("memorandumAndArticles", "Memorandum and articles of association", 125, FieldRequirement.CM, Document: true),
        new("resolutionBoardPoA", "Resolution from Board of Directors and Power of Attorney granted to manager/officer/employees", 125, FieldRequirement.CM, Document: true),
        new("namesSeniorManagement", "Names of persons holding senior management positions (list enclosed)", 125, FieldRequirement.CM, Document: true),
        new("certificateOfCommencement", "Certificate of Commencement of Business for Public Limited Companies", 125, FieldRequirement.CM, Document: true),
        new("othersCompany", "Others", 125, FieldRequirement.O, Document: true),
    };

    private static readonly Field[] Legal30Partnership =
    {
        Legal30Company[0], Legal30Company[1],
        new("registrationCertificate", "Registration certificate", 125, FieldRequirement.CM, Document: true),
        new("registrationNumber", "Registration number", 50, FieldRequirement.CM),
        new("llpinCertificate", "LLPIN Certificate", 125, FieldRequirement.CM, Document: true),
        new("llpin", "LLPIN", 7, FieldRequirement.CM),
        new("partnershipDeed", "Partnership Deed", 125, FieldRequirement.CM, Document: true),
        new("namesAllPartners", "Names of all partners (list to be enclosed)", 125, FieldRequirement.CM, Document: true),
        new("othersPartnership", "Others", 125, FieldRequirement.O, Document: true),
    };

    private static readonly Field[] Legal30Trust =
    {
        Legal30Company[0], Legal30Company[1],
        new("trustRegistrationCertificate", "Registration certificate", 125, FieldRequirement.CM, Document: true),
        new("trustRegistrationNumber", "Registration number", 50, FieldRequirement.CM),
        new("trustDeed", "Trust Deed", 125, FieldRequirement.CM, Document: true),
        new("namesBeneficiariesTrustees", "Names of all beneficiaries, Trustees, Settlor (Protector) and authors of the Trust", 125, FieldRequirement.CM, Document: true),
        new("trustPowerOfAttorney", "Power of Attorney granted to Beneficial owner, managers, officers or employee - Others", 125, FieldRequirement.O, Document: true),
        new("othersTrust", "Others", 125, FieldRequirement.O, Document: true),
    };

    private static readonly Field[] Legal30Unincorporated =
    {
        Legal30Company[0], Legal30Company[1],
        new("unincorporatedRegCertificate", "Registration certificate", 125, FieldRequirement.O, Document: true),
        new("unincorporatedRegNumber", "Registration number", 50, FieldRequirement.O),
        new("resolutionManagingBody", "Resolution of Managing Body or Body of Individuals of such association", 125, FieldRequirement.CM, Document: true),
        new("unincorporatedPowerOfAttorney", "Power of Attorney granted to transact on its behalf", 125, FieldRequirement.CM, Document: true),
        new("infoEstablishExistence", "Information required by the Reporting Entity to collectively establish existence of the association/body", 125, FieldRequirement.O, Document: true),
        new("othersUnincorporated", "Others", 125, FieldRequirement.O, Document: true),
    };

    private static readonly Field[] Legal30OtherTypes =
    {
        Legal30Company[0], Legal30Company[1],
        new("supportingDocumentsPoi", "Supporting Documents for PoI", 125, FieldRequirement.CM, Document: true),
        new("otherTypeRegistrationNumber", "Registration number", 50, FieldRequirement.O),
        new("otherTypeRegistrationCertificate", "Registration Certificate", 125, FieldRequirement.O, Document: true),
        new("otherTypePowerOfAttorney", "Power of attorney granted to its manager, officers or employees", 125, FieldRequirement.O, Document: true),
        new("activityProof1", "Activity Proof-1", 125, FieldRequirement.O, Document: true),
        new("activityProof2", "Activity Proof-2", 125, FieldRequirement.O, Document: true),
        new("othersOtherType", "Others", 125, FieldRequirement.O, Document: true),
    };

    // ------------------------------------------------------------------
    // Legal Entity record 40 — Registered office + principal place of business.
    // ------------------------------------------------------------------
    private static readonly Field[] Legal40 =
    {
        new("registeredAddressFlg", "Registered Address Details update flg", 1, FieldRequirement.O, Flag: true),
        new("regLine1", "Registered office address line 1", 60, FieldRequirement.CM),
        new("regLine2", "Registered office address line 2", 60, FieldRequirement.O),
        new("regLine3", "Registered office address line 3", 60, FieldRequirement.O),
        new("regCity", "Registered City/town/village", 60, FieldRequirement.CM),
        new("regState", "Registered State", 6, FieldRequirement.CM),
        new("regDistrict", "Registered District", 6, FieldRequirement.CM),
        new("regPinCode", "Registered Pin Code", 6, FieldRequirement.CM),
        new("regPinOthers", "Registered Pin code (in case of others)", 6, FieldRequirement.CM),
        new("regDigipin", "Registered DigiPIN", 10, FieldRequirement.O),
        new("regCountry", "ISO 3166 Country Code", 20, FieldRequirement.CM),
        new("regProofOfAddress", "Proof of address", 1, FieldRequirement.CM),                     // a/b/c
        new("regOtherDocumentName", "Other document name (registered address)", 50, FieldRequirement.CM),
        new("principalPlaceFlg", "principal place of business update flg", 1, FieldRequirement.O, Flag: true),
        new("principalSameAsRegistered", "Same as registered address", 1, FieldRequirement.CM),
        new("prinLine1", "Principal place of business line 1", 60, FieldRequirement.CM),
        new("prinLine2", "Principal place of business line 2", 60, FieldRequirement.O),
        new("prinLine3", "Principal place of business line 3", 60, FieldRequirement.O),
        new("prinCity", "Principal City/town/village", 60, FieldRequirement.CM),
        new("prinState", "Principal State", 2, FieldRequirement.CM),
        new("prinDistrict", "Principal District", 2, FieldRequirement.CM),
        new("prinPinCode", "Principal Pin Code", 6, FieldRequirement.CM),
        new("prinPinOthers", "Principal Pin code (in case of others)", 6, FieldRequirement.CM),
        new("prinDigipin", "Principal DigiPIN", 10, FieldRequirement.O),
        new("prinCountry", "Principal ISO 3166 Country Code", 20, FieldRequirement.CM),
        new("prinProofOfAddress", "Principal Proof of address", 1, FieldRequirement.CM),
        new("prinOtherDocumentName", "Principal other document name", 50, FieldRequirement.CM),
        new("regDocument", "Document for registered address", 125, FieldRequirement.O, Document: true),
        new("prinDocument", "Document for principal place of business address", 125, FieldRequirement.O, Document: true),
    };

    // ------------------------------------------------------------------
    // Legal Entity record 50 — two mobiles first, then emails (sheet '50').
    // ------------------------------------------------------------------
    private static readonly Field[] Legal50 =
    {
        new("contactDetailsFlg", "Contact details update flg", 1, FieldRequirement.O, Flag: true),
        new("countryCode1", "Country code (01)", 6, FieldRequirement.CM),
        new("mobileNumber1", "Mobile number (01)", 15, FieldRequirement.CM),
        new("countryCode2", "Country code (02)", 6, FieldRequirement.O),
        new("mobileNumber2", "Mobile number (02)", 15, FieldRequirement.O),
        new("email1", "Email ID (01)", 254, FieldRequirement.CM),
        new("email2", "Email ID (02)", 254, FieldRequirement.O),
        new("telephoneOfficial", "Telephone (official)", 12, FieldRequirement.O),
        new("fax", "FAX", 12, FieldRequirement.O),
    };

    // ------------------------------------------------------------------
    // Legal Entity record 60 — Related Party / beneficial owners (sheet '60').
    // ------------------------------------------------------------------
    private static readonly Field[] Legal60 =
    {
        new("relatedPartyFlg", "Related party details update flg", 1, FieldRequirement.O, Flag: true),
        new("entityConstitutionRelated", "Enitity Constituion", 60, FieldRequirement.CM),
        new("numberOfRelatedPersons", "Number of related Persons", 3, FieldRequirement.CM),
        new("numberOfBeneficialOwners", "W/w No of Beneficial Owner", 3, FieldRequirement.CM),
        new("relation", "Relation", 60, FieldRequirement.CM),
        new("relatedCkycNumber", "CKYC Number", 14, FieldRequirement.CM),
        new("controllingInterest", "Controlling interest", 50, FieldRequirement.CM),              // Ownership / other means
        new("percentageOwnership", "Percentage of Ownership/Exercise", 10, FieldRequirement.CM),
        new("otherRelationName", "Other Relation name", 33, FieldRequirement.CM),
        new("din", "DIN", 8, FieldRequirement.CM),
    };

    // ------------------------------------------------------------------
    // Legal Entity record 70 — Other Details and Attestation (sheet '70').
    // ------------------------------------------------------------------
    private static readonly Field[] Legal70 =
    {
        new("otherDetailsFlg", "Other details update flg", 1, FieldRequirement.O, Flag: true),
        new("remarks", "Remarks", 200, FieldRequirement.O),
        new("documentsReceivedFlg", "documents update flg", 1, FieldRequirement.O, Flag: true),
        new("certifiedCopies", "Certified copies", 1, FieldRequirement.CM),
        new("equivalentEDocument", "Equivalent e-document", 1, FieldRequirement.CM),
        new("verificationFromDigiLocker", "Verification from digilocker", 1, FieldRequirement.CM),
        new("attestationFlg", "Attestation update flg", 1, FieldRequirement.O, Flag: true),
        new("attestationDate", "Attestation Date", 10, FieldRequirement.CM, Date: true),
        new("employeeName", "Emp - name", 99, FieldRequirement.CM),
        new("employeeCode", "Emp code", 50, FieldRequirement.CM),
        new("employeeDesignation", "Emp designation", 50, FieldRequirement.CM),
        new("employeeBranch", "Emp branch", 50, FieldRequirement.CM),
        new("employeeCkycId", "Emp CKYC Id", 14, FieldRequirement.CM),
        new("institutionName", "Institution Name", 99, FieldRequirement.CM),
        new("institutionCode", "Institution Code", 6, FieldRequirement.CM),
        new("declarationDocument", "Declaration Document", 125, FieldRequirement.O, Document: true),
        new("declarationFlag", "Declaration Flag", 1, FieldRequirement.O),
        new("consentDocument", "Consent Document", 125, FieldRequirement.O, Document: true),
        new("place", "Place", 40, FieldRequirement.O),
        new("declarationDate", "Date", 10, FieldRequirement.O, Date: true),
    };

    /// <summary>
    /// All detail layouts grouped by (client type, record type). Record type "30" for client
    /// type "L" yields five constitution-specific variants; every other key yields exactly one.
    /// </summary>
    public static readonly ILookup<(string ClientType, string RecordType), Layout> DetailLayouts =
        BuildDetailLayouts().ToLookup(l => (l.ClientType, l.RecordType));

    private static List<Layout> BuildDetailLayouts() => new List<Layout>
    {
            new("I", "20", "Demographic Details", Individual20),
            new("I", "30", "Proof of Identity and Address", Individual30),
            new("I", "40", "Address Details", Individual40),
            new("I", "50", "Contact Details", Individual50),
            new("I", "60", "Related Party Details", Individual60),
            new("I", "70", "Other Details and Attestation", Individual70),

            new("L", "20", "Entity Details", Legal20),
            new("L", "30", "Proof of Identity and Address (Company)", Legal30Company),
            new("L", "30", "Proof of Identity and Address (Partnership Firm / LLP)", Legal30Partnership),
            new("L", "30", "Proof of Identity and Address (Trust)", Legal30Trust),
            new("L", "30", "Proof of Identity and Address (Unincorporated Association)", Legal30Unincorporated),
            new("L", "30", "Proof of Identity and Address (Other Constitution Types)", Legal30OtherTypes),
            new("L", "40", "Address Details", Legal40),
            new("L", "50", "Contact Details", Legal50),
            new("L", "60", "Related Party Details", Legal60),
            new("L", "70", "Other Details and Attestation", Legal70),
        };
}
