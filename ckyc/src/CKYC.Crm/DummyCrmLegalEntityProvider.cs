using System.Security.Cryptography;
using System.Text;
using CKYC.Core.Domain;

namespace CKYC.Crm;

/// <summary>
/// Deterministic fake CRM dataset for legal entities. It is the stand-in that will be
/// replaced by the real CRM integration later — the shape of <see cref="LegalEntity"/>
/// is exactly what the real CRM must produce for the client type "L".
/// </summary>
public sealed class DummyCrmLegalEntityProvider
{
    public LegalEntity GetLegalEntity(string customerId, string? constitution = null)
    {
        var idx = StableIndex(customerId);
        var constit = constitution ?? (idx % 2 == 0 ? LeConstitution.PrivateLimitedCompany : LeConstitution.Trust);
        var pan = $"AAACX{StableDigits(customerId + "P", 4)}A";
        var searchKey = $"LMO{StableDigits(customerId + "S", 17)}"; // must be exactly 20 chars (ERR_061)

        var proof = new LeProofOfIdentity();
        if (LeConstitution.IsCompany(constit))
        {
            proof.CertificateOfIncorporation = "CertificateOfIncorporation.pdf";
            proof.Cin = $"U{StableDigits(customerId + "C1", 5)}MH{2000 + idx % 20}PLC{StableDigits(customerId + "C2", 6)}";
            proof.MemorandumAndArticles = "MemorandumAndArticles.pdf";
            proof.ResolutionBoardPoA = "ResolutionPoA.pdf";
            proof.NamesSeniorManagement = "NamesSeniorManagement.pdf";
            if (constit is LeConstitution.PublicLimitedCompany)
                proof.CertificateOfCommencement = "CertificateOfCommencement.pdf";
        }
        else if (constit is LeConstitution.PartnershipFirm or LeConstitution.Llp)
        {
            proof.RegistrationCertificate = "PartnershipRegistration.pdf";
            proof.RegistrationNumber = $"REG{StableDigits(customerId + "R", 8)}";
            if (constit is LeConstitution.Llp)
            {
                proof.LlpinCertificate = "LLPINCertificate.pdf";
                proof.Llpin = $"A{StableDigits(customerId + "L", 6)}";
            }
            proof.PartnershipDeed = "PartnershipDeed.pdf";
            proof.NamesAllPartners = "NamesAllPartners.pdf";
        }
        else if (constit is LeConstitution.Trust)
        {
            proof.TrustRegistrationCertificate = "TrustRegistrationCertificate.pdf";
            proof.TrustRegistrationNumber = $"TR{StableDigits(customerId + "T", 6)}";
            proof.TrustDeed = "TrustDeed.pdf";
            proof.NamesBeneficiariesTrustees = "NamesBeneficiariesTrustees.pdf";
            proof.TrustPowerOfAttorney = "TrustPowerOfAttorney.pdf";
        }
        else if (constit is LeConstitution.UnincorporatedAssociation)
        {
            proof.UnincorporatedRegistrationCertificate = "AssociationRegistration.pdf";
            proof.UnincorporatedRegistrationNumber = $"ASC{StableDigits(customerId + "U", 8)}";
            proof.ResolutionManagingBody = "ManagingBodyResolution.pdf";
            proof.UnincorporatedPowerOfAttorney = "AssociationPowerOfAttorney.pdf";
        }
        else
        {
            proof.SupportingDocumentsPoi = "SupportingPoI.pdf";
            proof.OtherTypeRegistrationNumber = $"OTH{StableDigits(customerId + "O", 8)}";
            proof.OtherTypeRegistrationCertificate = "RegistrationCertificate.pdf";
            proof.OtherTypePowerOfAttorney = "PowerOfAttorney.pdf";
            if (constit is LeConstitution.SoleProprietorship) proof.ActivityProof1 = "ActivityProof1.pdf";
        }

        return new LegalEntity
        {
            CustomerId = customerId,
            SearchKey = searchKey,
            EntityName = $"Meridian Legal Entity {idx % 100}",
            EntityConstitution = constit,
            ListedCompany = constit is LeConstitution.PublicLimitedCompany ? "N" : null,
            RegisteredFirm = constit is LeConstitution.PartnershipFirm ? "Y" : null,
            RegisteredTrust = constit is LeConstitution.Trust ? "Y" : "N",
            DateOfIncorporation = $"{(idx % 28 + 1):00}-{(idx % 12 + 1):00}-{2000 + idx % 20}",
            DateOfCommencement = constit is LeConstitution.PublicLimitedCompany ? $"{(idx % 28 + 1):00}-{(idx % 12 + 1):00}-{2000 + idx % 20}" : null,
            PlaceOfIncorporation = "Mumbai",
            CountryOfIncorporation = "IN",
            TinIssuingCountry = "IN",
            Pan = pan,
            PanVerified = "Y",
            PanDocument = "Pan.pdf",
            TinGstNumber = $"22{pan}1Z5",
            TinGstnDocument = "TinGst.pdf",
            Proofs = new List<LeProofOfIdentity> { proof },
            RegisteredAddress = new LeAddressDetails
            {
                Line1 = "A-301 Tech Park", Line2 = "Andheri East", Line3 = "Mumbai",
                City = "Mumbai", State = "MH", District = "225", PinCode = "400069",
                Country = "IN", ProofOfAddress = "A", OtherDocumentName = null,
            },
            PrincipalAddress = new LeAddressDetails
            {
                SameAsRegistered = "N",
                Line1 = "B-505 Business Bay", Line2 = "Pune", Line3 = "",
                City = "Pune", State = "MH", District = "225", PinCode = "411014",
                Country = "IN", ProofOfAddress = "A", OtherDocumentName = null,
            },
            RegisteredAddressDocument = "RegAddress.pdf",
            PrincipalAddressDocument = "PrinAddress.pdf",
            Contact = new LeContactDetails
            {
                CountryCode1 = "+91", MobileNumber1 = $"98{StableDigits(customerId + "M", 8)}",
                CountryCode2 = "+91", MobileNumber2 = $"99{StableDigits(customerId + "N", 8)}",
                Email1 = $"contact{idx % 100}@meridiansoft.com",
                Email2 = $"legal{idx % 100}@meridiansoft.com",
                Telephone = $"022{StableDigits(customerId + "TEL", 8)}",
                Fax = $"022{StableDigits(customerId + "FAX", 8)}",
            },
            RelatedParties = new List<LeRelatedParty>
            {
                new()
                {
                    Relation = "Director",
                    CkycNumber = StableDigits(customerId + "R", 14),
                    ControllingInterest = "Ownership",
                    PercentageOwnership = $"{(idx % 50) + 10}.00",
                    Din = StableDigits(customerId + "D", 8),
                },
                new()
                {
                    Relation = "Beneficial Owner",
                    CkycNumber = StableDigits(customerId + "B", 14),
                    ControllingInterest = "Ownership",
                    PercentageOwnership = $"{(idx % 30) + 5}.00",
                },
            },
            Other = new LeOtherDetails
            {
                Remarks = "Fetched from CRM",
                CertifiedCopies = "Y", EquivalentEDoc = "N", VerificationFromDigiLocker = "N",
                AttestationDate = DateTime.Today.ToString("ddMMyyyy"),
                EmployeeName = "Anusaya", EmployeeCode = "A236", EmployeeDesignation = "SM",
                EmployeeBranch = "Kamlamills", EmployeeCkycId = StableDigits(customerId + "C", 14),
                InstitutionName = "PhonePe_Limited", InstitutionCode = "IN0238",
                DeclarationDocument = "D1.pdf", DeclarationFlag = "Y", ConsentDocument = "C3.pdf",
                Place = "Mumbai", DeclarationDate = DateTime.Today.ToString("dd-MM-yyyy"),
            },
        };
    }

    private static int StableIndex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? string.Empty));
        return BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
    }

    private static string StableDigits(string s, int length)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? string.Empty));
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++) sb.Append((char)('0' + bytes[i % bytes.Length] % 10));
        return sb.ToString();
    }
}
