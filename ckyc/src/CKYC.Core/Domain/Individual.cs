namespace CKYC.Core.Domain;

/// <summary>
/// A single customer's KYC details, populated from the (dummy) CRM and persisted
/// into the record tables (record types 20/30/40/50/60/70). The columns of each
/// record table mirror file format Excel field definitions.
/// </summary>
public sealed class Individual
{
    public long Id { get; set; }
    public long MasterRecordId { get; set; }
    /// <summary>Organization-owned customer key; never emitted as a CERSAI field.</summary>
    public string CustomerId { get; set; } = string.Empty;

    // ---- Record 20 : Demographics ----
    public string SearchKey { get; set; } = string.Empty;
    public string KycType { get; set; } = "N";
    public PersonName Name { get; set; } = new();
    public PersonName MaidenName { get; set; } = new();
    public PersonName MotherName { get; set; } = new();
    public PersonName FatherName { get; set; } = new();
    public PersonName SpouseName { get; set; } = new();
    public string? DateOfBirth { get; set; }              // DD-MM-YYYY
    public string? Gender { get; set; }                   // M / F / T
    public string? ResidentialStatus { get; set; }        // Resident | NRI | PIO | ForeignNational -> code A/B/C/D
    public string? ResidentialStatusSupportedByDocument { get; set; } = "Y";
    public string? Nationality { get; set; } = "IN";      // 2-letter country code, e.g. IN
    public string? NationalitySupportedByDocument { get; set; } = "Y";
    public string? DifferentlyAbledStatus { get; set; } = "N";   // PwD flag (Y/N)
    public string? DifferentlyAbledType { get; set; } = string.Empty;   // Type of Impairment code 01-21
    public string? Pan { get; set; }
    public string? PanVerified { get; set; }
    public string? PhotoOfIndividual { get; set; }

    // ---- Record 20 : conditional-mandatory fields (depend on other fields) ----
    public string? Minor { get; set; }                     // Y/N, derived from DOB when not supplied
    public string? DateOfBirthMatchWithOvd { get; set; }   // Y/N
    public string? NameMatchWithOvd { get; set; }          // Y/N
    public string? PhotoProvidedMatchWithOvd { get; set; } // Y/N
    public string? GenderProvidedInOvd { get; set; }       // Y/N
    public string? GenderMatchWithOvd { get; set; }        // Y/N (CM: mandatory when GenderProvidedInOvd = Y)
    public string? Form97Provided { get; set; }            // Form 97 (erstwhile Form 60) Y/N — one of PAN/Form60/Form61 required
    public string? Form61Provided { get; set; }            // Y/N
    public string? PanDocument { get; set; }               // optional PAN/Form 60/Form 61 document file name
    public string? OtherTypeOfImpairment { get; set; }     // CM: when Type of Impairment = 21 (Others)
    public string? DisabilityReferenceNumber { get; set; } // Certificate/UDID number (CM: when PwD = Y)
    public string? PermanentDisability { get; set; }       // Y/N (CM: when PwD = Y)
    public string? DisabilityDate { get; set; }            // DD-MM-YYYY (CM: when PermanentDisability = N)
    public string? PercentageOfImpairment { get; set; }    // 01-100 (CM: when PwD = Y)
    public string? DifferentlyAbledSupportedByDocument { get; set; } // Y/N (CM: when PwD = Y)

    // ---- Record 30 : Proof of Identity & Address (OVD) ----
    public List<ProofOfIdentity> Proofs { get; set; } = new();

    // ---- Record 40 : Addresses ----
    public AddressDetails? PermanentAddress { get; set; }
    /// <summary>
    /// CKYC record-40 "Same as permanent address" flag (Y/N). When Y, current-address
    /// text and proof fields are not applicable; the mandatory verification flags remain.
    /// </summary>
    public string? CurrentAddressSameAsPermanent { get; set; }
    public AddressDetails? CurrentAddress { get; set; }

    // ---- Record 50 : Contact ----
    public ContactDetails? Contact { get; set; }

    // ---- Record 60 : Related Party ----
    public List<RelatedParty> RelatedParties { get; set; } = new();

    // ---- Record 70 : Other Details & Attestation ----
    public OtherDetails? Other { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A titled person name block (record 20 Name sections).</summary>
public sealed class PersonName
{
    public string Title { get; set; } = string.Empty;     // Mr. / Ms. / Mrs. / Mx.
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public bool HasAnyName => !string.IsNullOrWhiteSpace(FirstName)
        || !string.IsNullOrWhiteSpace(MiddleName)
        || !string.IsNullOrWhiteSpace(LastName);
}

/// <summary>Proof of identity & address (record type 30).</summary>
public sealed class ProofOfIdentity
{
    public string OvdType { get; set; } = string.Empty;   // A Passport, B Voter, D DL, E Aadhaar, ...
    public string ModeOfAadhaarVerification { get; set; } = string.Empty;
    public string? PassportExpiryDate { get; set; }
    public string? DrivingLicenseExpiryDate { get; set; }
    public string? LengthOfAadhaar { get; set; }
    public string? IdNumber { get; set; }
    public string? CertifiedCopyWithOriginal { get; set; }
    public string? EquivalentEDoc { get; set; }
    public string? VerifiedFromDigiLocker { get; set; }
    public string? PresenceInMeaRepository { get; set; }
    public string? PresenceInEciRepository { get; set; }
    public string? PresenceInRtoRepository { get; set; }
    public string? PresenceInNregaRepository { get; set; }
    public string? PresenceInNprRecords { get; set; }
    public string? DataFromOfflineVerification { get; set; }
    public string? ModeOfAuthentication { get; set; }
    public string? EkycDataFromUidai { get; set; }
    public string? CopyOfOvd { get; set; }
}

/// <summary>Address block (record type 40).</summary>
public sealed class AddressDetails
{
    public string Line1 { get; set; } = string.Empty;
    public string Line2 { get; set; } = string.Empty;
    public string Line3 { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;   // 2-letter ISO code, e.g. IN
    public string State { get; set; } = string.Empty;     // state code
    public string District { get; set; } = string.Empty;  // district code
    public string City { get; set; } = string.Empty;
    public string PinCode { get; set; } = string.Empty;
    public string? PinCodeOthers { get; set; }
    public string? Digipin { get; set; }
    public string AddressSupportedWithDocument { get; set; } = "Y";
    public string AddressMatchWithOvd { get; set; } = "Y";

    // ---- Record 40 : current-address proof-of-address block (CM, when address differs) ----
    public string? ProofOfAddress { get; set; }            // 1 OVD, 2 Deemed POA, 3 Declared Address
    public string? ProofOfAddressType { get; set; }        // A/B/D/E/F/G/H
    public string? LengthOfAadhaar { get; set; }
    public string? IdNumber { get; set; }
    public string? ModeOfAadhaarVerification { get; set; }
    public string? OvdExpiryDate { get; set; }             // Driving Licence / Passport expiry date
    public string? DeemedPoa { get; set; }                 // 01-05
    public string? DeemedPoaVerified { get; set; }         // Y/N
    public string? CertifiedCopyWithOriginal { get; set; }
    public string? EquivalentEDoc { get; set; }
    public string? VerifiedFromDigiLocker { get; set; }
    public string? RemoteGeoTagging { get; set; }
    public string? AddressExactlyMatch { get; set; }       // Exact Match / No Match / Partial Match
    public string? PositiveVerification { get; set; }
    public string? PhysicalVerificationByThirdParty { get; set; }
    public string? PhysicalVerificationByReOfficial { get; set; }
    public string? PresenceInRepository { get; set; }
    public string? ForeignGovernmentDocument { get; set; }
    public string? CopyOfOvd { get; set; }
}

/// <summary>Contact details (record type 50).</summary>
public sealed class ContactDetails
{
    public string Email { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "+91";
    public string MobileNumber { get; set; } = string.Empty;
    public string? MobileValidatedViaOtp { get; set; }
    public string? EmailValidatedViaOtp { get; set; }
    public string? MobileValidatedViaThirdParty { get; set; }
}

/// <summary>Related party / guardian (record type 60).</summary>
public sealed class RelatedParty
{
    public string RelatedPersonType { get; set; } = string.Empty; // Guardian | Assignee | Authorized Representative
    public string CkycNumberOfRelatedPerson { get; set; } = string.Empty;
}

/// <summary>Other details & attestation (record type 70).</summary>
public sealed class OtherDetails
{
    public string Remarks { get; set; } = string.Empty;
    public string VideoKycWithoutOfficial { get; set; } = "N";
    public string VideoKycWithReOfficial { get; set; } = "N";
    public string FaceToFaceWithReOfficial { get; set; } = "N";
    public string NonFaceToFace { get; set; } = "N";
    public string FaceToFaceWithNonOfficial { get; set; } = "N";
    public string AttestationDate { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeDesignation { get; set; } = string.Empty;
    public string EmployeeBranch { get; set; } = string.Empty;
    public string EmployeeCkycId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string DeclarationDocument { get; set; } = string.Empty;
    public string DeclarationFlag { get; set; } = "Y";
    public string ClientConsent { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public string DeclarationDate { get; set; } = string.Empty;
}
