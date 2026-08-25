using System.Text;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Spec;

namespace CKYC.Files;

/// <summary>
/// Writes a CKYC legal-entity bulk-upload (.UPL) file — a pipe-delimited text file with
/// the exact record-10/20/30/40/50/60/70 field layouts for client type "L" that the
/// CERSAI FVU validates. Mirrors <see cref="CkycUploadWriter"/> for the individual/retail
/// client type, writing to the SAME header shape but with Entity Details (record 20),
/// constitution-specific POI (record 30), registered + principal addresses (record 40),
/// two-mobile/two-email contact (record 50), beneficial-owner related parties (record 60)
/// and attestation (record 70).
///
/// The source of truth is the "File_Format_Upload_LegalEntity" Excel.
/// </summary>
public sealed class CkycLegalEntityUploadWriter
{
    private readonly BatchSettings _batch;

    public CkycLegalEntityUploadWriter(BatchSettings batch) => _batch = batch;

    public string Write(IReadOnlyList<LegalEntity> records, DateOnly businessDate)
    {
        var sb = new StringBuilder();
        var lineNo = 1;

        sb.AppendLine(BuildHeader(businessDate, records.Count));

        foreach (var record in records)
        {
            var r20Line = lineNo++;
            sb.AppendLine(BuildRecord20(record, r20Line));

            // Record 30 : one POI line for the entity (the applicable constitution block).
            if (record.Proofs.Count > 0)
                sb.AppendLine(BuildRecord30(record.Proofs[0], r20Line, lineNo++));

            if (record.RegisteredAddress is not null)
                sb.AppendLine(BuildRecord40(record, r20Line, lineNo++));

            if (record.Contact is not null)
                sb.AppendLine(BuildRecord50(record.Contact, r20Line, lineNo++));

            foreach (var rp in record.RelatedParties)
                sb.AppendLine(BuildRecord60(record, rp, r20Line, lineNo++));

            if (record.Other is not null)
                sb.AppendLine(BuildRecord70(record, r20Line, lineNo++, businessDate));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maps each customer id to the record-20 "Line Number" written in this file so
    /// a CERSAI reply can be attributed back to the right master record.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ComputeRecord20Lines(IReadOnlyList<LegalEntity> records)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lineNo = 1;
        foreach (var r in records)
        {
            map[r.CustomerId] = lineNo;
            lineNo += 1;                                                  // record-20
            if (r.Proofs.Count > 0) lineNo += 1;                          // record-30
            if (r.RegisteredAddress is not null) lineNo += 1;             // record-40
            if (r.Contact is not null) lineNo += 1;                       // record-50
            lineNo += r.RelatedParties.Count;                             // record-60
            if (r.Other is not null) lineNo += 1;                         // record-70
        }
        return map;
    }

    private string BuildHeader(DateOnly businessDate, int count)
    {
        var f = new string?[11];
        f[0] = CkycRecords.Header;                    // 10
        f[1] = _batch.FiCode;
        f[2] = _batch.RegionCode;
        f[3] = "L";                                   // Client Type — Legal Entity
        f[4] = count.ToString();
        f[5] = _batch.VersionNumber;
        f[6] = businessDate.ToString("dd-MM-yyyy");
        f[7] = f[8] = f[9] = f[10] = "";
        return string.Join('|', f);
    }

    private static string BuildRecord20(LegalEntity r, int lineNo)
    {
        var f = new string?[24];
        f[0] = CkycRecords.Demographic;               // 20
        f[1] = lineNo.ToString();
        f[2] = NormalizeSearchKey(r.SearchKey);
        f[3] = r.EntityName;
        f[4] = r.EntityConstitution;
        f[5] = Coalesce(r.ListedCompany, "N");
        f[6] = Coalesce(r.RegisteredFirm, "N");
        f[7] = Coalesce(r.RegisteredTrust, "N");
        f[8] = r.DateOfIncorporation;
        f[9] = Coalesce(r.DateOfCommencement, "");
        f[10] = Coalesce(r.PlaceOfIncorporation, "");
        f[11] = Coalesce(r.CountryOfIncorporation, "IN");
        f[12] = Coalesce(r.TinIssuingCountry, "");
        f[13] = Coalesce(r.Pan, "");
        f[14] = Coalesce(r.Form97, "");
        f[15] = Coalesce(r.TinGstNumber, "");
        f[16] = Coalesce(r.PanDocument, "");
        f[17] = Coalesce(r.PanVerified, "Y");
        f[18] = Coalesce(r.TinGstnDocument, "");

        // Detail-record counts must match the records actually emitted below.
        f[19] = (r.Proofs.Count > 0) ? "1" : "0";                       // record 30
        f[20] = r.RegisteredAddress is not null ? "1" : "0";            // record 40
        f[21] = r.Contact is not null ? "1" : "0";                      // record 50
        f[22] = r.RelatedParties.Count.ToString();                      // record 60
        f[23] = r.Other is not null ? "1" : "0";                        // record 70
        return string.Join('|', f);
    }

    private static string BuildRecord30(LeProofOfIdentity p, int r20Line, int lineNo)
    {
        var f = new string?[36];
        f[0] = CkycRecords.Proof;                     // 30
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();

        // ---- Company / Section 8 ----
        f[3] = Coalesce(p.CertificateOfIncorporation, "");
        f[4] = Coalesce(p.Cin, "");
        f[5] = Coalesce(p.MemorandumAndArticles, "");
        f[6] = Coalesce(p.ResolutionBoardPoA, "");
        f[7] = Coalesce(p.NamesSeniorManagement, "");
        f[8] = Coalesce(p.CertificateOfCommencement, "");
        f[9] = Coalesce(p.OthersCompany, "");

        // ---- Partnership Firm / LLP ----
        f[10] = Coalesce(p.RegistrationCertificate, "");
        f[11] = Coalesce(p.RegistrationNumber, "");
        f[12] = Coalesce(p.LlpinCertificate, "");
        f[13] = Coalesce(p.Llpin, "");
        f[14] = Coalesce(p.PartnershipDeed, "");
        f[15] = Coalesce(p.NamesAllPartners, "");
        f[16] = Coalesce(p.OthersPartnership, "");

        // ---- Trust ----
        f[17] = Coalesce(p.TrustRegistrationCertificate, "");
        f[18] = Coalesce(p.TrustRegistrationNumber, "");
        f[19] = Coalesce(p.TrustDeed, "");
        f[20] = Coalesce(p.NamesBeneficiariesTrustees, "");
        f[21] = Coalesce(p.TrustPowerOfAttorney, "");
        f[22] = Coalesce(p.OthersTrust, "");

        // ---- Unincorporated Association ----
        f[23] = Coalesce(p.UnincorporatedRegistrationCertificate, "");
        f[24] = Coalesce(p.UnincorporatedRegistrationNumber, "");
        f[25] = Coalesce(p.ResolutionManagingBody, "");
        f[26] = Coalesce(p.UnincorporatedPowerOfAttorney, "");
        f[27] = Coalesce(p.InfoEstablishExistence, "");
        f[28] = Coalesce(p.OthersUnincorporated, "");

        // ---- Other constitution types ----
        f[29] = Coalesce(p.SupportingDocumentsPoi, "");
        f[30] = Coalesce(p.OtherTypeRegistrationNumber, "");
        f[31] = Coalesce(p.OtherTypeRegistrationCertificate, "");
        f[32] = Coalesce(p.OtherTypePowerOfAttorney, "");
        f[33] = Coalesce(p.ActivityProof1, "");
        f[34] = Coalesce(p.ActivityProof2, "");
        f[35] = Coalesce(p.OthersOtherType, "");

        return string.Join('|', f);
    }

    private static string BuildRecord40(LegalEntity r, int r20Line, int lineNo)
    {
        var reg = r.RegisteredAddress!;
        var prin = r.PrincipalAddress;
        var f = new string?[30];
        f[0] = CkycRecords.Address;                   // 40
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();

        // Registered office address.
        f[3] = reg.Line1; f[4] = reg.Line2; f[5] = reg.Line3;
        f[6] = reg.City; f[7] = reg.State; f[8] = reg.District;
        f[9] = reg.PinCode; f[10] = reg.PinCodeOthers; f[11] = reg.Digipin;
        f[12] = Coalesce(reg.Country, "IN");
        f[13] = Coalesce(reg.ProofOfAddress, "A");
        f[14] = Coalesce(reg.OtherDocumentName, "");

        // Principal place of business.
        f[15] = prin?.SameAsRegistered ?? (prin is null ? "Y" : "N");
        if (prin is not null)
        {
            f[16] = prin.Line1; f[17] = prin.Line2; f[18] = prin.Line3;
            f[19] = prin.City; f[20] = prin.State; f[21] = prin.District;
            f[22] = prin.PinCode; f[23] = prin.PinCodeOthers; f[24] = prin.Digipin;
            f[25] = Coalesce(prin.Country, "IN");
            f[26] = Coalesce(prin.ProofOfAddress, "A");
            f[27] = Coalesce(prin.OtherDocumentName, "");
        }

        // Supporting documents.
        f[28] = Coalesce(r.RegisteredAddressDocument, "RegAddress.pdf");
        f[29] = Coalesce(r.PrincipalAddressDocument, "PrinAddress.pdf");

        return string.Join('|', f);
    }

    private static string BuildRecord50(LeContactDetails c, int r20Line, int lineNo)
    {
        var f = new string?[11];
        f[0] = CkycRecords.Contact;                   // 50
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();
        f[3] = Coalesce(c.CountryCode1, "+91");
        f[4] = Coalesce(c.MobileNumber1, "");
        f[5] = Coalesce(c.CountryCode2, "+91");
        f[6] = Coalesce(c.MobileNumber2, "");
        f[7] = Coalesce(c.Email1, "");
        f[8] = Coalesce(c.Email2, "");
        f[9] = Coalesce(c.Telephone, "");
        f[10] = Coalesce(c.Fax, "");
        return string.Join('|', f);
    }

    private static string BuildRecord60(LegalEntity record, LeRelatedParty rp, int r20Line, int lineNo)
    {
        var f = new string?[11];
        f[0] = CkycRecords.RelatedParty;              // 60
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();
        f[3] = record.RelatedParties.Count.ToString();
        f[4] = record.RelatedParties.Count(x => string.Equals(x.Relation?.Trim(), "Beneficial Owner", StringComparison.OrdinalIgnoreCase)).ToString();
        f[5] = rp.Relation;
        f[6] = Coalesce(rp.CkycNumber, "");
        f[7] = Coalesce(rp.ControllingInterest, "");
        f[8] = Coalesce(rp.PercentageOwnership, "");
        f[9] = Coalesce(rp.OtherRelationName, "");
        f[10] = Coalesce(rp.Din, "");
        return string.Join('|', f);
    }

    private static string BuildRecord70(LegalEntity r, int r20Line, int lineNo, DateOnly businessDate)
    {
        var o = r.Other!;
        var f = new string?[20];
        f[0] = CkycRecords.Other;                     // 70
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();
        f[3] = Coalesce(o.Remarks, "");
        f[4] = Coalesce(o.CertifiedCopies, "Y");
        f[5] = Coalesce(o.EquivalentEDoc, "N");
        f[6] = Coalesce(o.VerificationFromDigiLocker, "N");
        f[7] = Coalesce(o.AttestationDate, businessDate.ToString("ddMMyyyy"));
        f[8] = Coalesce(o.EmployeeName, "");
        f[9] = Coalesce(o.EmployeeCode, "");
        f[10] = Coalesce(o.EmployeeDesignation, "");
        f[11] = Coalesce(o.EmployeeBranch, "");
        f[12] = Coalesce(o.EmployeeCkycId, "");
        f[13] = Coalesce(o.InstitutionName, "");
        f[14] = Coalesce(o.InstitutionCode, "");
        f[15] = Coalesce(o.DeclarationDocument, "");
        f[16] = Coalesce(o.DeclarationFlag, "Y");
        f[17] = Coalesce(o.ConsentDocument, "");
        f[18] = Coalesce(o.Place, "");
        f[19] = Coalesce(o.DeclarationDate, businessDate.ToString("dd-MM-yyyy"));
        return string.Join('|', f);
    }

    private static string NormalizeSearchKey(string? searchKey)
    {
        if (string.IsNullOrEmpty(searchKey)) return new string('0', 20);
        if (searchKey.Length == 20) return searchKey;
        if (searchKey.Length > 20) return searchKey[..20];
        return searchKey.PadRight(20, '0');
    }

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
