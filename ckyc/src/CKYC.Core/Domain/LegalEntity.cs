namespace CKYC.Core.Domain;

/// <summary>
/// A single legal entity's CKYC details, populated from the (dummy) CRM and persisted
/// into the dedicated legal-entity record tables (record types 20/30/40/50/60/70).
/// The columns of each table mirror the "File_Format_Upload_LegalEntity" Excel fields.
///
/// These tables are deliberately SEPARATE from the individual record tables
/// (<c>kyc_record_*</c>) — a legal entity and a retail customer never share a row.
/// </summary>
public sealed class LegalEntity
{
    public long Id { get; set; }
    public long MasterRecordId { get; set; }
    /// <summary>Organization-owned customer key; never emitted as a CERSAI field.</summary>
    public string CustomerId { get; set; } = string.Empty;

    // ---- Record 20 : Entity Details ----
    public string SearchKey { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;                 // Name of the legal entity
    public string EntityConstitution { get; set; } = string.Empty;        // A..R (see LeConstitution)
    public string? ListedCompany { get; set; }                            // Y/N (public listed co only)
    public string? RegisteredFirm { get; set; }                           // Y/N (partnership)
    public string? RegisteredTrust { get; set; }                          // Y/N (trust)
    public string? DateOfIncorporation { get; set; }                      // DD-MM-YYYY
    public string? DateOfCommencement { get; set; }                       // DD-MM-YYYY (public listed co)
    public string? PlaceOfIncorporation { get; set; }
    public string? CountryOfIncorporation { get; set; } = "IN";
    public string? TinIssuingCountry { get; set; }
    public string? Pan { get; set; }                                      // 10-char PAN
    public string? Form97 { get; set; }                                   // Y/null when PAN is absent
    public string? TinGstNumber { get; set; }                             // 15-char TIN/GST
    public string? PanDocument { get; set; }                              // support doc file name
    public string? PanVerified { get; set; }                              // Y/N
    public string? TinGstnDocument { get; set; }                          // support doc file name

    // ---- Record 30 : Proof of Identity & Address (POI) ----
    public List<LeProofOfIdentity> Proofs { get; set; } = new();

    // ---- Record 40 : Address (registered office + principal place of business) ----
    public LeAddressDetails? RegisteredAddress { get; set; }
    public LeAddressDetails? PrincipalAddress { get; set; }
    public string? RegisteredAddressDocument { get; set; }
    public string? PrincipalAddressDocument { get; set; }

    // ---- Record 50 : Contact ----
    public LeContactDetails? Contact { get; set; }

    // ---- Record 60 : Related Party ----
    public List<LeRelatedParty> RelatedParties { get; set; } = new();

    // ---- Record 70 : Other Details & Attestation ----
    public LeOtherDetails? Other { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Entity constitution codes (record-20 "Entity Constitution" dropdown, A..R),
/// with helpers for the constitution branches that drive the CM rules.
/// </summary>
public static class LeConstitution
{
    public const string SoleProprietorship = "A";
    public const string PartnershipFirm = "B";
    public const string Huf = "C";
    public const string PrivateLimitedCompany = "D";
    public const string PublicLimitedCompany = "E";
    public const string Society = "F";
    public const string UnincorporatedAssociation = "G";
    public const string Trust = "H";
    public const string Liquidator = "I";
    public const string Llp = "J";
    public const string ArtificialLiabilityPartnership = "K";
    public const string PublicSectorBank = "L";
    public const string GovernmentDepartment = "M";
    public const string Section8Company = "N";
    public const string ArtificialJuridicalPerson = "O";
    public const string InternationalOrganisation = "P";
    public const string ForeignPortfolioInvestor = "Q";
    public const string Others = "R";

    /// <summary>Company-type constitutions for which the company POI section applies.</summary>
    public static bool IsCompany(string constitution)
        => constitution is PrivateLimitedCompany or PublicLimitedCompany or Section8Company;

    /// <summary>Whether a CIN is mandatory for the constitution.</summary>
    public static bool RequiresCin(string constitution) => IsCompany(constitution);

    /// <summary>Whether a beneficial-owner related party is mandatory.</summary>
    public static bool RequiresBeneficialOwner(string constitution)
        => constitution is PrivateLimitedCompany or PublicLimitedCompany or PartnershipFirm or Llp or Trust or UnincorporatedAssociation or Society;
}

/// <summary>Proof of identity &amp; address documents (record type 30) for a legal entity.</summary>
public sealed class LeProofOfIdentity
{
    // ---- Company / Section 8 (constitution D/E/N) ----
    public string? CertificateOfIncorporation { get; set; }
    public string? Cin { get; set; }
    public string? MemorandumAndArticles { get; set; }
    public string? ResolutionBoardPoA { get; set; }
    public string? NamesSeniorManagement { get; set; }
    public string? CertificateOfCommencement { get; set; }     // public limited companies
    public string? OthersCompany { get; set; }

    // ---- Partnership Firm / LLP (B / J) ----
    public string? RegistrationCertificate { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? LlpinCertificate { get; set; }
    public string? Llpin { get; set; }
    public string? PartnershipDeed { get; set; }
    public string? NamesAllPartners { get; set; }
    public string? OthersPartnership { get; set; }

    // ---- Trust (H) ----
    public string? TrustRegistrationCertificate { get; set; }
    public string? TrustRegistrationNumber { get; set; }
    public string? TrustDeed { get; set; }
    public string? NamesBeneficiariesTrustees { get; set; }
    public string? TrustPowerOfAttorney { get; set; }
    public string? OthersTrust { get; set; }

    // ---- Unincorporated Association / Body of Individuals (G) ----
    public string? UnincorporatedRegistrationCertificate { get; set; }
    public string? UnincorporatedRegistrationNumber { get; set; }
    public string? ResolutionManagingBody { get; set; }
    public string? UnincorporatedPowerOfAttorney { get; set; }
    public string? InfoEstablishExistence { get; set; }
    public string? OthersUnincorporated { get; set; }

    // ---- Other constitution types (all others) ----
    public string? SupportingDocumentsPoi { get; set; }
    public string? OtherTypeRegistrationNumber { get; set; }
    public string? OtherTypeRegistrationCertificate { get; set; }
    public string? OtherTypePowerOfAttorney { get; set; }
    public string? ActivityProof1 { get; set; }                // sole proprietorship
    public string? ActivityProof2 { get; set; }                // sole proprietorship
    public string? OthersOtherType { get; set; }
}

/// <summary>Address block (record type 40) for a legal entity — registered office or principal place.</summary>
public sealed class LeAddressDetails
{
    public string Line1 { get; set; } = string.Empty;
    public string Line2 { get; set; } = string.Empty;
    public string Line3 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;          // state code
    public string District { get; set; } = string.Empty;       // district code
    public string PinCode { get; set; } = string.Empty;
    public string? PinCodeOthers { get; set; }
    public string? Digipin { get; set; }
    public string Country { get; set; } = "IN";
    public string ProofOfAddress { get; set; } = "A";          // A certificate of incorporation, B registration certificate, C other
    public string? OtherDocumentName { get; set; }
    public string? SameAsRegistered { get; set; }              // Y/N — principal place of business only
}

/// <summary>Contact details (record type 50) for a legal entity.</summary>
public sealed class LeContactDetails
{
    public string? CountryCode1 { get; set; } = "+91";
    public string? MobileNumber1 { get; set; }
    public string? CountryCode2 { get; set; } = "+91";
    public string? MobileNumber2 { get; set; }
    public string? Email1 { get; set; }
    public string? Email2 { get; set; }
    public string? Telephone { get; set; }
    public string? Fax { get; set; }
}

/// <summary>Related party / beneficial owner (record type 60) for a legal entity.</summary>
public sealed class LeRelatedParty
{
    public string Relation { get; set; } = string.Empty;       // Director/Promoter/Karta/...
    public string CkycNumber { get; set; } = string.Empty;     // 14-char CKYC number
    public string ControllingInterest { get; set; } = string.Empty; // Ownership | Through other means
    public string? PercentageOwnership { get; set; }
    public string? OtherRelationName { get; set; }
    public string? Din { get; set; }                           // Director Identification Number
}

/// <summary>Other details &amp; attestation (record type 70) for a legal entity.</summary>
public sealed class LeOtherDetails
{
    public string? Remarks { get; set; }
    public string CertifiedCopies { get; set; } = "Y";
    public string EquivalentEDoc { get; set; } = "N";
    public string VerificationFromDigiLocker { get; set; } = "N";
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
    public string ConsentDocument { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public string DeclarationDate { get; set; } = string.Empty;
}
