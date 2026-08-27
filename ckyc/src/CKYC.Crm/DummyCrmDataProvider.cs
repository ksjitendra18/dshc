using System.Security.Cryptography;
using System.Text;
using CKYC.Core.Domain;

namespace CKYC.Crm;

/// <summary>
/// Deterministic fake CRM dataset for the demo. It is the stand-in that will be
/// replaced by the real CRM integration later — the shape of <see cref="Individual"/>
/// is exactly what the real CRM must produce.
/// </summary>
public sealed class DummyCrmDataProvider
{
    private static readonly (string Title, string First, string Middle, string Last)[] Names =
    {
        ("Mr.", "Amrish", "", "Puri"),
        ("Ms.", "Priya", "", "Sharma"),
        ("Mr.", "Rahul", "Kumar", "Verma"),
        ("Mrs.", "Anjali", "", "Mehta"),
        ("Mr.", "Suresh", "K", "Iyer"),
        ("Ms.", "Neha", "", "Gupta"),
        ("Mr.", "Vikram", "Singh", "Chauhan"),
        ("Mrs.", "Kavita", "R", "Patel"),
        ("Mr.", "Arjun", "", "Nair"),
        ("Ms.", "Deepa", "M", "Reddy"),
        ("Mr.", "Gaurav", "", "Joshi"),
        ("Mrs.", "Pooja", "S", "Kulkarni"),
    };

    public Individual GetCustomer(string customerId)
    {
        var idx = StableIndex(customerId);
        var name = Names[idx % Names.Length];
        var mobile = $"98{StableDigits(customerId + "M", 8)}";
        var email = $"{name.First.ToLowerInvariant()}.{name.Last.ToLowerInvariant()}@yopmail.com";
        var searchKey = $"IMO{StableDigits(customerId + "S", 17)}"; // must be exactly 20 chars (ERR_061)

        return new Individual
        {
            CustomerId = customerId,
            SearchKey = searchKey,
            KycType = "N",
            Name = new PersonName { Title = name.Title, FirstName = name.First, MiddleName = name.Middle, LastName = name.Last },
            DateOfBirth = $"{(idx % 28 + 1):00}-{(idx % 12 + 1):00}-{1970 + idx % 40}",
            Gender = "M",
            ResidentialStatus = "Resident",
            ResidentialStatusSupportedByDocument = "Y",
            Nationality = "IN",
            NationalitySupportedByDocument = "Y",
            DifferentlyAbledStatus = "N",
            Pan = $"ABCP{StableDigits(customerId + "P", 5)}{"A"[0]}",
            PanVerified = "Y",
            PanDocument = "Pan.pdf",
            DateOfBirthMatchWithOvd = "Y",
            NameMatchWithOvd = "Y",
            PhotoProvidedMatchWithOvd = "Y",
            GenderProvidedInOvd = "Y",
            GenderMatchWithOvd = "Y",
            PhotoOfIndividual = "Photo.jpg",
            Proofs = new List<ProofOfIdentity>
            {
                new()
                {
                    OvdType = "E",                 // Aadhaar / VID
                    ModeOfAadhaarVerification = "B",
                    LengthOfAadhaar = "A",
                    IdNumber = StableDigits(customerId + "I", 4),   // 4-digit masked Aadhaar
                    CertifiedCopyWithOriginal = "",                 // blank for E + mode B (ERR_324)
                    ModeOfAuthentication = "A",                     // OTP (Aadhaar E-KYC mode)
                    EkycDataFromUidai = "Y",
                    CopyOfOvd = "AdhaarAP.jpg",
                },
            },
            PermanentAddress = new AddressDetails
            {
                Line1 = "B-109 Man Deep CHS LTD", Line2 = "Navghar Road", Line3 = "Saibaba Nagar",
                Country = "IN", State = "MH", District = "225", City = "Bhayandar",
                PinCode = "401106", AddressSupportedWithDocument = "N", AddressMatchWithOvd = "No Match",
            },
            CurrentAddressSameAsPermanent = "N",
            CurrentAddress = new AddressDetails
            {
                Line1 = "ABC", Line2 = "CCC", Line3 = "CCC",
                Country = "IN", State = "MH", District = "225", City = "BHayandar",
                PinCode = "401107", AddressSupportedWithDocument = "N", AddressMatchWithOvd = "No Match",
                ProofOfAddress = "1", ProofOfAddressType = "E", LengthOfAadhaar = "A",
                IdNumber = StableDigits(customerId + "I", 4), ModeOfAadhaarVerification = "B",
                CertifiedCopyWithOriginal = "Y", EquivalentEDoc = "N", VerifiedFromDigiLocker = "N",
                CopyOfOvd = "AdhaarAP.jpg", RemoteGeoTagging = "Y", AddressExactlyMatch = "Exact Match",
                PositiveVerification = "Y", PhysicalVerificationByThirdParty = "Y",
                PhysicalVerificationByReOfficial = "Y", PresenceInRepository = "Y",
            },
            Contact = new ContactDetails
            {
                Email = email, CountryCode = "+91", MobileNumber = mobile,
                MobileValidatedViaOtp = "Y", EmailValidatedViaOtp = "Y", MobileValidatedViaThirdParty = "Y",
            },
            RelatedParties = new List<RelatedParty>
            {
                new() { RelatedPersonType = "Assignee", CkycNumberOfRelatedPerson = StableDigits(customerId + "R", 14) },
            },
            Other = new OtherDetails
            {
                Remarks = "Fetched from CRM",
                VideoKycWithoutOfficial = "N", VideoKycWithReOfficial = "N",
                FaceToFaceWithReOfficial = "Y", FaceToFaceWithNonOfficial = "N", NonFaceToFace = "N",
                AttestationDate = DateTime.Today.ToString("dd-MM-yyyy"),
                EmployeeName = "Anusaya", EmployeeCode = "A236", EmployeeDesignation = "SM",
                EmployeeBranch = "Kamlamills", EmployeeCkycId = StableDigits(customerId + "C", 14),
                InstitutionName = "PhonePe_Limited", InstitutionCode = "IN0238",
                DeclarationDocument = "D1.pdf", DeclarationFlag = "Y", ClientConsent = "C3.pdf",
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
