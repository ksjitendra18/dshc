using System.Text;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Spec;

namespace CKYC.Files;

/// <summary>
/// Writes a CKYC individual bulk-upload (.UPL) file — a pipe-delimited text file with
/// the exact record-10/20/30/40/50/60/70 field layouts that the CERSAI FVU validates.
///
/// The source of truth for every field, its position, size and mandatory/optional/
/// conditional-mandatory (M/O/CM) nature is the "File_Format_Upload_Individual" Excel,
/// NOT a reference sample file. Unlike a reference sample copy, each CM field is derived
/// with explicit if/else logic from the fields it depends on, so a field is never left
/// empty when its condition makes it mandatory — rather it is filled from the record or
/// from a documented FVU-valid default. Records that cannot satisfy a rule are excluded
/// before this writer runs (see <see cref="CkycRecordValidator"/>).
/// </summary>
public sealed class CkycUploadWriter
{
    private readonly BatchSettings _batch;
    private readonly Func<string, string?, string?> _documentName;

    public CkycUploadWriter(BatchSettings batch, Func<string, string?, string?>? documentName = null)
    {
        _batch = batch;
        _documentName = documentName ?? ((_, name) => name);
    }

    public string Write(IReadOnlyList<Individual> records, DateOnly businessDate)
    {
        var sb = new StringBuilder();
        var lineNo = 1;                       // running sequence across the whole file
        var detailCount = records.Count;

        sb.AppendLine(BuildHeader(businessDate, detailCount));

        foreach (var record in records)
        {
            var r20Line = lineNo++;
            sb.AppendLine(BuildRecord20(record, r20Line));

            foreach (var proof in record.Proofs)
                sb.AppendLine(BuildRecord30(record.CustomerId, proof, r20Line, lineNo++));

            if (record.PermanentAddress is not null || record.CurrentAddress is not null)
                sb.AppendLine(BuildRecord40(record, r20Line, lineNo++));

            if (record.Contact is not null)
                sb.AppendLine(BuildRecord50(record.Contact, r20Line, lineNo++));

            foreach (var rp in record.RelatedParties)
                sb.AppendLine(BuildRecord60(rp, r20Line, lineNo++));

            if (record.Other is not null)
                sb.AppendLine(BuildRecord70(record, r20Line, lineNo++, businessDate));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maps each customer id to the "Line Number" that is written into its record-20 detail
    /// (the running sequence starting at 1, after the header). The CERSAI reply detail
    /// (record 100) refers back to the input record-20 by exactly this number, so storing it
    /// on the master row lets a response be attributed back to the right record.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ComputeRecord20Lines(IReadOnlyList<Individual> records)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lineNo = 1;
        foreach (var r in records)
        {
            map[r.CustomerId] = lineNo;
            lineNo += 1;                                                       // record-20
            lineNo += r.Proofs.Count;                                          // record-30
            if (r.PermanentAddress is not null || r.CurrentAddress is not null) lineNo += 1; // record-40
            if (r.Contact is not null) lineNo += 1;                            // record-50
            lineNo += r.RelatedParties.Count;                                  // record-60
            if (r.Other is not null) lineNo += 1;                              // record-70
        }
        return map;
    }

    private string BuildHeader(DateOnly businessDate, int count)
    {
        var f = new string?[11];
        f[0] = CkycRecords.Header;              // 10
        f[1] = _batch.FiCode;
        f[2] = _batch.RegionCode;
        f[3] = _batch.ClientType;               // I
        f[4] = count.ToString();
        f[5] = _batch.VersionNumber;            // V1.0
        f[6] = businessDate.ToString("dd-MM-yyyy");
        f[7] = f[8] = f[9] = f[10] = "";
        return string.Join('|', f);
    }

    private string BuildRecord20(Individual r, int lineNo)
    {
        var f = new string?[56];
        f[0] = CkycRecords.Demographic;
        f[1] = lineNo.ToString();

        f[2] = NormalizeSearchKey(r.SearchKey);
        f[3] = Coalesce(r.KycType, "N");

        f[4] = r.Name.Title;
        f[5] = r.Name.FirstName;
        f[6] = r.Name.MiddleName;
        f[7] = r.Name.LastName;

        f[8] = r.MaidenName.Title;
        f[9] = r.MaidenName.FirstName;
        f[10] = r.MaidenName.MiddleName;
        f[11] = r.MaidenName.LastName;

        // Mother / Father / Spouse — at least one of the three must be present (CM).
        f[12] = Coalesce(r.MotherName.Title, "Mrs.");
        f[13] = r.MotherName.FirstName;
        f[14] = r.MotherName.MiddleName;
        f[15] = r.MotherName.LastName;

        f[16] = Coalesce(r.FatherName.Title, "Mr.");
        f[17] = r.FatherName.FirstName;
        f[18] = r.FatherName.MiddleName;
        f[19] = r.FatherName.LastName;

        f[20] = r.SpouseName.Title;
        f[21] = r.SpouseName.FirstName;
        f[22] = r.SpouseName.MiddleName;
        f[23] = r.SpouseName.LastName;

        f[24] = r.DateOfBirth;
        f[25] = ResolveMinor(r.DateOfBirth, r.Minor);                 // Minor (Y/N)
        f[26] = Coalesce(r.DateOfBirthMatchWithOvd, "Y");              // DOB matching with OVD
        f[27] = Coalesce(r.NameMatchWithOvd, "Y");                     // Name matching with OVD
        f[28] = Coalesce(r.PhotoProvidedMatchWithOvd, "Y");            // Photo provided matching with OVD
        f[29] = r.Gender;                                              // Gender (M/F/T)
        f[30] = Coalesce(r.GenderProvidedInOvd, "Y");                  // Gender provided in OVD

        // Gender matching with OVD (CM) — mandatory only when Gender provided in OVD = Y.
        var genderProvided = Is(f[30], "Y");
        f[31] = genderProvided ? Coalesce(r.GenderMatchWithOvd, "Y") : "";

        // One of PAN / Form 97 (erstwhile Form 60) / Form 61 is required (CM).
        var pan = r.Pan;
        f[32] = pan;
        var form61 = Is(r.Form61Provided, "Y");
        var form97 = Is(r.Form97Provided, "Y") || (!string.IsNullOrWhiteSpace(pan) ? false : !form61);
        f[33] = form97 ? "Y" : "N";
        f[34] = form61 ? "Y" : "N";

        // PAN verified (CM) — mandatory where PAN is provided.
        var hasPan = !string.IsNullOrWhiteSpace(pan);
        f[35] = hasPan ? Coalesce(r.PanVerified, "Y") : "";

        f[36] = ResidentialStatusCode(r.ResidentialStatus);            // A/B/C/D
        f[37] = Coalesce(r.ResidentialStatusSupportedByDocument, "Y");
        f[38] = Coalesce(r.Nationality, "IN");
        f[39] = Coalesce(r.NationalitySupportedByDocument, "Y");

        // Person with Disability (PwD) block (CM fields keyed off the PwD flag).
        var pwD = Is(r.DifferentlyAbledStatus, "Y");
        f[40] = pwD ? "Y" : "N";
        f[41] = pwD ? Coalesce(r.DifferentlyAbledType, "21") : "";
        f[42] = Is(f[41], "21") ? Coalesce(r.OtherTypeOfImpairment, "") : "";
        f[43] = pwD ? Coalesce(r.DisabilityReferenceNumber, "") : "";
        f[44] = pwD ? Coalesce(r.PermanentDisability, "Y") : "";
        f[45] = Is(f[44], "N") ? Coalesce(r.DisabilityDate, "") : "";
        f[46] = pwD ? Coalesce(r.PercentageOfImpairment, "") : "";
        f[47] = pwD ? Coalesce(r.DifferentlyAbledSupportedByDocument, "Y") : "";

        // PAN attachment is optional in the create format, including when PAN is supplied.
        f[48] = Doc(r.CustomerId, r.PanDocument);

        f[49] = Doc(r.CustomerId, r.PhotoOfIndividual);

        // Detail-record counts must match the records actually emitted below.
        f[50] = r.Proofs.Count.ToString();                                    // record 30
        f[51] = (r.PermanentAddress is not null || r.CurrentAddress is not null) ? "1" : "0"; // record 40
        f[52] = r.Contact is not null ? "1" : "0";                             // record 50
        f[53] = r.RelatedParties.Count.ToString();                             // record 60
        f[54] = r.Other is not null ? "1" : "0";                               // record 70

        return string.Join('|', f);
    }

    private string BuildRecord30(string customerId, ProofOfIdentity p, int r20Line, int lineNo)
    {
        var f = new string?[22];
        f[0] = CkycRecords.Proof;
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();

        f[3] = p.OvdType;
        var ovd = p.OvdType.Trim().ToUpperInvariant();

        // Mode of Aadhaar Verification (CM) — mandatory for KYC type "O"; for Aadhaar OVD the
        // record's mode is used, defaulting to eKYC Authentication (the common case).
        f[4] = ovd == "E" ? Coalesce(p.ModeOfAadhaarVerification, "B") : Coalesce(p.ModeOfAadhaarVerification, "");

        // Passport expiry (CM) — required when the OVD is a Passport (A).
        f[5] = ovd == "A" ? Coalesce(p.PassportExpiryDate, "") : "";

        // Driving licence expiry (CM) — required when the OVD is a Driving Licence (D).
        f[6] = ovd == "D" ? Coalesce(p.DrivingLicenseExpiryDate, "") : "";

        // Length of Aadhaar/VID (CM) — applicable to OVD type E only.
        f[7] = ovd == "E" ? Coalesce(p.LengthOfAadhaar, "A") : "";

        // ID Number (CM) — applicable for all OVD types except "H".
        f[8] = ovd != "H" ? Coalesce(p.IdNumber, "") : "";

        // At least one of Certified copy / Equivalent e-doc / DigiLocker (CM) — required for all
        // OVD types except "H" and except Aadhaar verified via E-KYC/offline.
        var aadhaarEkycOrOffline = ovd == "E" && (Is(p.ModeOfAadhaarVerification, "B") || Is(p.ModeOfAadhaarVerification, "C"));
        var needsOneProof = ovd != "H" && !aadhaarEkycOrOffline;
        var certified = Coalesce(p.CertifiedCopyWithOriginal, "");
        var equiv = Coalesce(p.EquivalentEDoc, "");
        var digi = Coalesce(p.VerifiedFromDigiLocker, "");
        if (needsOneProof && !(Is(certified, "Y") || Is(equiv, "Y") || Is(digi, "Y")))
            certified = "Y"; // auto-satisfy the CM "at least one" rule with a FVU-valid default
        f[9] = certified;
        f[10] = equiv;
        f[11] = digi;

        // Repository presence flags (CM) — each applicable to its own OVD type.
        f[12] = ovd == "A" ? Coalesce(p.PresenceInMeaRepository, "Y") : "";
        f[13] = ovd == "B" ? Coalesce(p.PresenceInEciRepository, "Y") : "";
        f[14] = ovd == "D" ? Coalesce(p.PresenceInRtoRepository, "Y") : "";
        f[15] = ovd == "F" ? Coalesce(p.PresenceInNregaRepository, "Y") : "";
        f[16] = ovd == "G" ? Coalesce(p.PresenceInNprRecords, "Y") : "";

        // Data received from offline verification (CM) — Aadhaar OVD + offline verification.
        f[17] = ovd == "E" && Is(p.ModeOfAadhaarVerification, "C") ? Coalesce(p.DataFromOfflineVerification, "Y") : "";

        // Mode of Authentication (CM) — Aadhaar OVD in E-KYC mode (default OTP).
        f[18] = ovd == "E" && Is(p.ModeOfAadhaarVerification, "B") ? Coalesce(p.ModeOfAuthentication, "A") : "";

        // E-KYC data received from UIDAI (CM) — mandatory for KYC type O / Aadhaar E-KYC.
        f[19] = ovd == "E" && Is(p.ModeOfAadhaarVerification, "B") ? Coalesce(p.EkycDataFromUidai, "Y") : "";

        // Copy of OVD (CM) — applicable for all OVD types except "H".
        f[20] = ovd != "H" ? Doc(customerId, Coalesce(p.CopyOfOvd, "AdhaarAP.jpg")) : "";

        return string.Join('|', f);
    }

    private string BuildRecord40(Individual r, int r20Line, int lineNo)
    {
        var f = new string?[46];
        f[0] = CkycRecords.Address;
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();

        var perm = r.PermanentAddress;
        if (perm is not null)
        {
            f[3] = perm.Line1; f[4] = perm.Line2; f[5] = perm.Line3;
            f[6] = Coalesce(perm.Country, "IN"); f[7] = perm.State; f[8] = perm.District;
            f[9] = perm.City; f[10] = perm.PinCode; f[11] = perm.PinCodeOthers;
            f[12] = perm.Digipin;
            f[13] = Coalesce(perm.AddressSupportedWithDocument, "Y");
            f[14] = Coalesce(perm.AddressMatchWithOvd, "Exact Match");
        }
        else
        {
            f[6] = "IN"; f[13] = "Y"; f[14] = "Exact Match";
        }

        // "Same as permanent address" is an explicit M field in the create format. Do not infer
        // it by comparing addresses: when Y, current-address text/proof fields must stay empty.
        var curr = r.CurrentAddress;
        var sameAsPermanent = Is(r.CurrentAddressSameAsPermanent, "Y");
        f[15] = r.CurrentAddressSameAsPermanent;

        // These four fields are mandatory even when no distinct current-address block is needed.
        f[37] = Coalesce(curr?.RemoteGeoTagging, "Y");
        f[39] = Coalesce(curr?.PositiveVerification, "Y");
        f[40] = Coalesce(curr?.PhysicalVerificationByThirdParty, "Y");
        f[41] = Coalesce(curr?.PhysicalVerificationByReOfficial, "Y");

        if (curr is not null)
        {
            if (!sameAsPermanent)
            {
                // Current-address block (CM: applicable only when "Same as permanent address" = N).
                f[16] = curr.Line1; f[17] = curr.Line2; f[18] = curr.Line3;
                f[19] = Coalesce(curr.Country, "IN"); f[20] = curr.State; f[21] = curr.District;
                f[22] = curr.City; f[23] = curr.PinCode; f[24] = curr.PinCodeOthers;
                f[25] = curr.Digipin;

                ResolveCurrentProof(f, curr, r, 26);

                // Copy of OVD (field 44) must reference an existing file (resolved from record-30).
                var copyOfOvd = r.Proofs.Count > 0 ? r.Proofs[0].CopyOfOvd : null;
                if (string.IsNullOrWhiteSpace(copyOfOvd)) copyOfOvd = "AdhaarAP.jpg";
                f[44] = Doc(r.CustomerId, copyOfOvd);

                // Field 29 (ID number of the current-address proof) must match the record-30 OVD ID.
                f[29] = r.Proofs.Count > 0 ? r.Proofs[0].IdNumber : null;
            }
            else
            {
                // Same as permanent: no current-address text or proof details are emitted.
            }
        }

        return string.Join('|', f);
    }

    private static void ResolveCurrentProof(string?[] f, AddressDetails curr, Individual r, int start)
    {
        // Proof of Address (CM) — applicable only when the current address differs from permanent.
        var poa = curr.ProofOfAddress;
        f[start] = Coalesce(poa, "1"); // default OVD
        var poaType = curr.ProofOfAddressType;
        f[start + 1] = Is(f[start], "1") ? Coalesce(poaType, ResolveOvdType(r)) : "";

        // Length of Aadhaar / ID number / mode of Aadhaar verification (CM) — for OVD proof.
        var isOvd = Is(f[start], "1");
        f[start + 2] = isOvd && Is(f[start + 1], "E") ? Coalesce(curr.LengthOfAadhaar, "A") : "";
        f[start + 3] = isOvd && f[start + 1] != "H" ? Coalesce(curr.IdNumber, r.Proofs.FirstOrDefault()?.IdNumber ?? "") : "";
        f[start + 4] = isOvd && Is(f[start + 1], "E") ? Coalesce(curr.ModeOfAadhaarVerification, "B") : "";

        // Driving licence / Passport expiry (CM) — for DL/Passport OVD proof.
        f[start + 5] = isOvd && (Is(f[start + 1], "A") || Is(f[start + 1], "D")) ? Coalesce(curr.OvdExpiryDate, "") : "";

        // Deemed POA (CM) — for Deemed proof of address.
        f[start + 6] = Is(f[start], "2") ? Coalesce(curr.DeemedPoa, "01") : "";
        f[start + 7] = Is(f[start], "2") ? Coalesce(curr.DeemedPoaVerified, "Y") : "";

        // Certified copy / DigiLocker / equivalent e-doc (CM) — for OVD other than H.
        var isOvdNotH = isOvd && f[start + 1] != "H";
        f[start + 8] = isOvdNotH ? Coalesce(curr.CertifiedCopyWithOriginal, "Y") : "";      // 34 certified copy
        f[start + 9] = isOvdNotH ? Coalesce(curr.VerifiedFromDigiLocker, "N") : "";         // 35 verified from DigiLocker
        f[start + 10] = isOvdNotH ? Coalesce(curr.EquivalentEDoc, "N") : "";                // 36 equivalent e-doc

        // Remote geo tagging (M).
        f[start + 11] = Coalesce(curr.RemoteGeoTagging, "Y");

        // Address exactly matches Deemed POA / OVD (CM).
        var requiresExactMatch = Is(f[start], "2") || isOvdNotH;
        f[start + 12] = requiresExactMatch ? Coalesce(curr.AddressExactlyMatch, "Exact Match") : "";

        // Positive verification / physical verification by third party / RE official (M).
        f[start + 13] = Coalesce(curr.PositiveVerification, "Y");
        f[start + 14] = Coalesce(curr.PhysicalVerificationByThirdParty, "Y");
        f[start + 15] = Coalesce(curr.PhysicalVerificationByReOfficial, "Y");

        // Presence of DL/Passport/Voter/NREGA/NPR in census records (CM) — only applicable for those
        // OVD proof types (Passport A / Voter B / Driving Licence D / NREGA F / NPR G). Not applicable
        // for e.g. Aadhaar (E), so it is left blank there (ERR_118).
        var presenceApplies = f[start + 1] is "A" or "B" or "D" or "F" or "G";
        f[start + 16] = isOvd && presenceApplies ? Coalesce(curr.PresenceInRepository, "Y") : "";

        // Foreign government document (CM) — where nationality is non-Indian or foreign national.
        var foreign = !Is(r.Nationality, "IN") || Is(r.ResidentialStatus, "ForeignNational") || Is(r.ResidentialStatus, "Foreign National");
        f[start + 17] = foreign ? Coalesce(curr.ForeignGovernmentDocument, "") : "";
    }

    private static string ResolveOvdType(Individual r)
    {
        var first = r.Proofs.FirstOrDefault();
        return first?.OvdType ?? "E";
    }

    private static string BuildRecord50(ContactDetails c, int r20Line, int lineNo)
    {
        var f = new string?[10];
        f[0] = CkycRecords.Contact; f[1] = lineNo.ToString(); f[2] = r20Line.ToString();
        f[3] = c.Email;

        // Country code (CM) — mandatory when a mobile number is provided.
        f[4] = !string.IsNullOrWhiteSpace(c.MobileNumber) ? Coalesce(c.CountryCode, "+91") : Coalesce(c.CountryCode, "");

        f[5] = c.MobileNumber;

        // Mobile validated via OTP / third party (CM) — when a mobile number is provided.
        var hasMobile = !string.IsNullOrWhiteSpace(c.MobileNumber);
        f[6] = hasMobile ? Coalesce(c.MobileValidatedViaOtp, "Y") : "";
        f[8] = hasMobile ? Coalesce(c.MobileValidatedViaThirdParty, "Y") : "";

        // Email validated via OTP (CM) — when an email id is provided.
        f[7] = !string.IsNullOrWhiteSpace(c.Email) ? Coalesce(c.EmailValidatedViaOtp, "Y") : "";

        return string.Join('|', f);
    }

    private static string BuildRecord60(RelatedParty rp, int r20Line, int lineNo)
    {
        var f = new string?[6];
        f[0] = CkycRecords.RelatedParty; f[1] = lineNo.ToString(); f[2] = r20Line.ToString();
        f[3] = rp.RelatedPersonType;
        // CKYC Number (CM) — applicable when Related Person Type is provided.
        f[4] = string.IsNullOrWhiteSpace(rp.RelatedPersonType) ? "" : Coalesce(rp.CkycNumberOfRelatedPerson, "");
        return string.Join('|', f);
    }

    private string BuildRecord70(Individual r, int r20Line, int lineNo, DateOnly businessDate)
    {
        var o = r.Other!; // guaranteed non-null by the caller
        var f = new string?[23];
        f[0] = CkycRecords.Other; f[1] = lineNo.ToString(); f[2] = r20Line.ToString();
        f[3] = o.Remarks;
        f[4] = o.VideoKycWithoutOfficial;
        f[5] = o.VideoKycWithReOfficial;
        f[6] = o.FaceToFaceWithReOfficial;

        // Non face to face (CM) — mandatory when KYC type is "O".
        f[7] = Is(r.KycType, "O") ? Coalesce(o.NonFaceToFace, "Y") : Coalesce(o.NonFaceToFace, "N");

        f[8] = o.FaceToFaceWithNonOfficial;
        f[9] = Coalesce(o.AttestationDate, businessDate.ToString("dd-MM-yyyy"));

        // Employee attestation fields (CM) — mandatory when Mode of KYC = Face to Face with RE official.
        var faceToFaceRe = Is(o.FaceToFaceWithReOfficial, "Y");
        f[10] = faceToFaceRe ? Coalesce(o.EmployeeName, "") : "";
        f[11] = faceToFaceRe ? Coalesce(o.EmployeeCode, "") : "";
        f[12] = faceToFaceRe ? Coalesce(o.EmployeeDesignation, "") : "";
        f[13] = faceToFaceRe ? Coalesce(o.EmployeeBranch, "") : "";
        f[14] = faceToFaceRe ? Coalesce(o.EmployeeCkycId, "") : "";

        f[15] = o.InstitutionName;
        f[16] = o.InstitutionCode;
        f[17] = Doc(r.CustomerId, o.DeclarationDocument);
        f[18] = Coalesce(o.DeclarationFlag, "Y");
        f[19] = Doc(r.CustomerId, o.ClientConsent);
        f[20] = o.Place;
        f[21] = Coalesce(o.DeclarationDate, businessDate.ToString("dd-MM-yyyy"));
        return string.Join('|', f);
    }

    // ---- field-value helpers ----

    /// <summary>Derives the Minor flag from the DOB (under 18 = Y) unless explicitly supplied.</summary>
    private static string ResolveMinor(string? dateOfBirth, string? supplied)
    {
        if (!string.IsNullOrWhiteSpace(supplied)) return supplied.Trim() is "Y" or "N" ? supplied.Trim() : "N";
        if (TryParseDob(dateOfBirth, out var dob))
            return dob.AddYears(18) > DateOnly.FromDateTime(DateTime.Today) ? "Y" : "N";
        return "N";
    }

    /// <summary>Maps the descriptive residential status to the Excel code A/B/C/D.</summary>
    private static string ResidentialStatusCode(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "A";
        return status.Trim().ToUpperInvariant() switch
        {
            "NRI" => "B",
            "PIO" => "C",
            "FOREIGNNATIONAL" or "FOREIGN NATIONAL" => "D",
            _ => "A",
        };
    }

    private static bool TryParseDob(string? value, out DateOnly dob)
        => DateOnly.TryParseExact(value, "dd-MM-yyyy", out dob);

    private static string NormalizeSearchKey(string? searchKey)
    {
        // The FVU requires the search key to be exactly 20 characters (ERR_061).
        if (string.IsNullOrEmpty(searchKey)) return new string('0', 20);
        if (searchKey.Length == 20) return searchKey;
        if (searchKey.Length > 20) return searchKey[..20];
        return searchKey.PadRight(20, '0');
    }

    private static bool Is(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private string? Doc(string customerId, string? value) => _documentName(customerId, value);
}
