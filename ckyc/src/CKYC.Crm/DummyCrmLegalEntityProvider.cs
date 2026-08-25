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
        var pan = $"AAAC{StableDigits(customerId + "P", 5)}{"A"[0]}";
        var searchKey = $"LMO{StableDigits(customerId + "S", 17)}"; // must be exactly 20 chars (ERR_061)

        var proof = new LeProofOfIdentity();
        if (constit is LeConstitution.PrivateLimitedCompany)
        {
            proof.CertificateOfIncorporation = "CertificateOfIncorporation.pdf";
            proof.Cin = $"U{StableDigits(customerId + "C", 20)}";
            proof.MemorandumAndArticles = "MemorandumAndArticles.pdf";
            proof.ResolutionBoardPoA = "ResolutionPoA.pdf";
            proof.NamesSeniorManagement = "NamesSeniorManagement.pdf";
        }
        else
        {
            proof.TrustRegistrationCertificate = "TrustRegistrationCertificate.pdf";
            proof.TrustRegistrationNumber = $"TR{StableDigits(customerId + "T", 6)}";
            proof.TrustDeed = "TrustDeed.pdf";
            proof.NamesBeneficiariesTrustees = "NamesBeneficiariesTrustees.pdf";
            proof.TrustPowerOfAttorney = "TrustPowerOfAttorney.pdf";
        }

        return new LegalEntity
        {
            SourceCustomerId = customerId,
            SearchKey = searchKey,
            EntityName = constit is LeConstitution.PrivateLimitedCompany
                ? $"Meridian Software Pvt Ltd {idx % 100}"
                : $"Sunrise Charitable Trust {idx % 100}",
            EntityConstitution = constit,
            ListedCompany = constit is LeConstitution.PrivateLimitedCompany ? "N" : null,
            RegisteredFirm = "N",
            RegisteredTrust = constit is LeConstitution.Trust ? "Y" : "N",
            DateOfIncorporation = $"{(idx % 28 + 1):00}-{(idx % 12 + 1):00}-{2000 + idx % 20}",
            DateOfCommencement = constit is LeConstitution.PrivateLimitedCompany ? $"{(idx % 28 + 1):00}-{(idx % 12 + 1):00}-{2000 + idx % 20}" : null,
            PlaceOfIncorporation = constit is LeConstitution.PrivateLimitedCompany ? "Mumbai" : "Pune",
            CountryOfIncorporation = "IN",
            TinIssuingCountry = "IN",
            Pan = pan,
            PanVerified = "Y",
            PanDocument = "Pan.pdf",
            TinGstNumber = $"22{StableDigits(customerId + "G", 13)}",
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
                AttestationDate = DateTime.Today.ToString("dd-MM-yyyy"),
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
