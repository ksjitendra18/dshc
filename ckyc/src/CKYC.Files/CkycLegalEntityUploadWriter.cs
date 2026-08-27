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
    private readonly Func<string, string?, string?> _documentName;

    public CkycLegalEntityUploadWriter(BatchSettings batch, Func<string, string?, string?>? documentName = null)
    {
        _batch = batch;
        _documentName = documentName ?? ((_, name) => name);
    }

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
                sb.AppendLine(BuildRecord30(record.CustomerId, record, r20Line, lineNo++));

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

    // Every detail record ends with an empty "Hash Value" placeholder column that the
    // CERSAI FVU overwrites with the record-level hash ("Hash Value" is the last row of
    // every record sheet in File_Format_Upload_LegalEntity, and the official L_*.UPL
    // sample carries a trailing pipe on every detail line — see vendor/sample_files).

    private string BuildRecord20(LegalEntity r, int lineNo)
    {
        var f = new string?[25];   // 24 spec fields + Hash Value placeholder
        f[0] = CkycRecords.Demographic;               // 20
        f[1] = lineNo.ToString();
        f[2] = NormalizeSearchKey(r.SearchKey);
        f[3] = r.EntityName;
        f[4] = r.EntityConstitution;
        // The FVU rejects these flags when they do not apply to the constitution
        // (ERR_252/ERR_257), so they are emitted only for their own branch.
        f[5] = Is(r.EntityConstitution, LeConstitution.PublicLimitedCompany) ? Coalesce(r.ListedCompany, "N") : "";
        f[6] = Is(r.EntityConstitution, LeConstitution.PartnershipFirm) ? Coalesce(r.RegisteredFirm, "N") : "";
        f[7] = Is(r.EntityConstitution, LeConstitution.Trust) ? Coalesce(r.RegisteredTrust, "N") : "";
        f[8] = r.DateOfIncorporation;
        f[9] = Coalesce(r.DateOfCommencement, "");
        f[10] = Coalesce(r.PlaceOfIncorporation, "");
        f[11] = Coalesce(r.CountryOfIncorporation, "IN");
        f[12] = Coalesce(r.TinIssuingCountry, "");
        f[13] = Coalesce(r.Pan, "");
        f[14] = Coalesce(r.Form97, "");
        f[15] = Coalesce(r.TinGstNumber, "");
        f[16] = Doc(r.CustomerId, Coalesce(r.PanDocument, ""));
        f[17] = Coalesce(r.PanVerified, "Y");
        f[18] = Doc(r.CustomerId, Coalesce(r.TinGstnDocument, ""));

        // Detail-record counts must match the records actually emitted below.
        f[19] = (r.Proofs.Count > 0) ? "1" : "0";                       // record 30
        f[20] = r.RegisteredAddress is not null ? "1" : "0";            // record 40
        f[21] = r.Contact is not null ? "1" : "0";                      // record 50
        f[22] = r.RelatedParties.Count.ToString();                      // record 60
        f[23] = r.Other is not null ? "1" : "0";                        // record 70
        return string.Join('|', f);
    }

    /// <summary>
    /// Record 30 is constitution-specific: only the block that applies to the entity's
    /// constitution is written (immediately after the reference record-20 column),
    /// followed by the Hash Value placeholder. The FVU enforces the exact pipe count
    /// per POI section (ERR_169: e.g. Company POI = 10 pipes, Trust POI = 9 pipes).
    /// </summary>
    private string BuildRecord30(string customerId, LegalEntity record, int r20Line, int lineNo)
    {
        var p = record.Proofs[0];
        var f = new string?[11];      // RT + line + ref + applicable block + Hash Value placeholder
        f[0] = CkycRecords.Proof;                     // 30
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();

        if (LeConstitution.IsCompany(record.EntityConstitution))
        {
            f[3] = Doc(customerId, Coalesce(p.CertificateOfIncorporation, ""));
            f[4] = Coalesce(p.Cin, "");
            f[5] = Doc(customerId, Coalesce(p.MemorandumAndArticles, ""));
            f[6] = Doc(customerId, Coalesce(p.ResolutionBoardPoA, ""));
            f[7] = Doc(customerId, Coalesce(p.NamesSeniorManagement, ""));
            f[8] = Doc(customerId, Coalesce(p.CertificateOfCommencement, ""));
            f[9] = Doc(customerId, Coalesce(p.OthersCompany, ""));
        }
        else if (record.EntityConstitution is LeConstitution.PartnershipFirm or LeConstitution.Llp)
        {
            f[3] = Doc(customerId, Coalesce(p.RegistrationCertificate, ""));
            f[4] = Coalesce(p.RegistrationNumber, "");
            f[5] = Doc(customerId, Coalesce(p.LlpinCertificate, ""));
            f[6] = Coalesce(p.Llpin, "");
            f[7] = Doc(customerId, Coalesce(p.PartnershipDeed, ""));
            f[8] = Doc(customerId, Coalesce(p.NamesAllPartners, ""));
            f[9] = Doc(customerId, Coalesce(p.OthersPartnership, ""));
        }
        else if (Is(record.EntityConstitution, LeConstitution.Trust))
        {
            // Trust block has one field fewer — trimmed to keep the FVU pipe count.
            var trust = new string?[10];
            trust[0] = CkycRecords.Proof;
            trust[1] = lineNo.ToString();
            trust[2] = r20Line.ToString();
            trust[3] = Doc(customerId, Coalesce(p.TrustRegistrationCertificate, ""));
            trust[4] = Coalesce(p.TrustRegistrationNumber, "");
            trust[5] = Doc(customerId, Coalesce(p.TrustDeed, ""));
            trust[6] = Doc(customerId, Coalesce(p.NamesBeneficiariesTrustees, ""));
            trust[7] = Doc(customerId, Coalesce(p.TrustPowerOfAttorney, ""));
            trust[8] = Doc(customerId, Coalesce(p.OthersTrust, ""));
            return string.Join('|', trust);
        }
        else if (Is(record.EntityConstitution, LeConstitution.UnincorporatedAssociation))
        {
            var unincorporated = new string?[10];
            unincorporated[0] = CkycRecords.Proof;
            unincorporated[1] = lineNo.ToString();
            unincorporated[2] = r20Line.ToString();
            unincorporated[3] = Doc(customerId, Coalesce(p.UnincorporatedRegistrationCertificate, ""));
            unincorporated[4] = Coalesce(p.UnincorporatedRegistrationNumber, "");
            unincorporated[5] = Doc(customerId, Coalesce(p.ResolutionManagingBody, ""));
            unincorporated[6] = Doc(customerId, Coalesce(p.UnincorporatedPowerOfAttorney, ""));
            unincorporated[7] = Doc(customerId, Coalesce(p.InfoEstablishExistence, ""));
            unincorporated[8] = Doc(customerId, Coalesce(p.OthersUnincorporated, ""));
            return string.Join('|', unincorporated);
        }
        else
        {
            f[3] = Doc(customerId, Coalesce(p.SupportingDocumentsPoi, ""));
            f[4] = Coalesce(p.OtherTypeRegistrationNumber, "");
            f[5] = Doc(customerId, Coalesce(p.OtherTypeRegistrationCertificate, ""));
            f[6] = Doc(customerId, Coalesce(p.OtherTypePowerOfAttorney, ""));
            f[7] = Doc(customerId, Coalesce(p.ActivityProof1, ""));
            f[8] = Doc(customerId, Coalesce(p.ActivityProof2, ""));
            f[9] = Doc(customerId, Coalesce(p.OthersOtherType, ""));
        }

        return string.Join('|', f);
    }

    private string BuildRecord40(LegalEntity r, int r20Line, int lineNo)
    {
        var reg = r.RegisteredAddress!;
        var prin = r.PrincipalAddress;
        var f = new string?[31];      // 30 spec fields + Hash Value placeholder
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

        // Principal place of business. The FVU requires every principal field to be blank
        // when 'Same as Registered Address' is Y (ERR_310/311/313/314/315/316/319/320),
        // even if the record carries default-filled principal details.
        var sameAsRegistered = prin?.SameAsRegistered ?? (prin is null ? "Y" : "N");
        f[15] = sameAsRegistered;
        if (prin is not null && !Is(sameAsRegistered, "Y"))
        {
            f[16] = prin.Line1; f[17] = prin.Line2; f[18] = prin.Line3;
            f[19] = prin.City; f[20] = prin.State; f[21] = prin.District;
            f[22] = prin.PinCode; f[23] = prin.PinCodeOthers; f[24] = prin.Digipin;
            f[25] = Coalesce(prin.Country, "IN");
            f[26] = Coalesce(prin.ProofOfAddress, "A");
            f[27] = Coalesce(prin.OtherDocumentName, "");
        }

        // Supporting documents. The registered-address document is always required; the
        // principal-address document only applies when 'Same as Registered Address' is N.
        f[28] = Doc(r.CustomerId, Coalesce(r.RegisteredAddressDocument, "RegAddress.pdf"));
        f[29] = Is(sameAsRegistered, "Y") ? "" : Doc(r.CustomerId, Coalesce(r.PrincipalAddressDocument, "PrinAddress.pdf"));

        return string.Join('|', f);
    }

    private static string BuildRecord50(LeContactDetails c, int r20Line, int lineNo)
    {
        var f = new string?[12];      // 11 spec fields + Hash Value placeholder
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
        var f = new string?[12];      // 11 spec fields + Hash Value placeholder
        f[0] = CkycRecords.RelatedParty;              // 60
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();
        f[3] = record.RelatedParties.Count.ToString();
        // 'W/w No of Beneficial Owner' is not applicable for constitutions that do not
        // maintain a beneficial-owner registry (FVU ERR_147 for constitution A) — the
        // count is emitted only for constitutions that require a beneficial owner.
        var beneficialOwners = record.RelatedParties.Count(x =>
            string.Equals(x.Relation?.Trim(), "Beneficial Owner", StringComparison.OrdinalIgnoreCase));
        f[4] = LeConstitution.RequiresBeneficialOwner(record.EntityConstitution) ? beneficialOwners.ToString() : "";
        f[5] = rp.Relation;
        f[6] = Coalesce(rp.CkycNumber, "");
        // Controlling interest / percentage of ownership apply only to Beneficial Owner
        // rows (FVU ERR_111/ERR_258) — they are blanked for every other relation.
        var isBeneficialOwner = string.Equals(rp.Relation?.Trim(), "Beneficial Owner", StringComparison.OrdinalIgnoreCase);
        f[7] = isBeneficialOwner ? Coalesce(rp.ControllingInterest, "") : "";
        f[8] = isBeneficialOwner ? Coalesce(rp.PercentageOwnership, "") : "";
        f[9] = Coalesce(rp.OtherRelationName, "");
        f[10] = Coalesce(rp.Din, "");
        return string.Join('|', f);
    }

    private string BuildRecord70(LegalEntity r, int r20Line, int lineNo, DateOnly businessDate)
    {
        var o = r.Other!;
        var f = new string?[21];      // 20 spec fields + Hash Value placeholder
        f[0] = CkycRecords.Other;                     // 70
        f[1] = lineNo.ToString();
        f[2] = r20Line.ToString();
        f[3] = Coalesce(o.Remarks, "");
        f[4] = Coalesce(o.CertifiedCopies, "Y");
        f[5] = Coalesce(o.EquivalentEDoc, "N");
        f[6] = Coalesce(o.VerificationFromDigiLocker, "N");
        // The FVU requires the KYC verification (attestation) date in DD-MM-YYYY (ERR_262).
        f[7] = Coalesce(o.AttestationDate, businessDate.ToString("dd-MM-yyyy"));
        f[8] = Coalesce(o.EmployeeName, "");
        f[9] = Coalesce(o.EmployeeCode, "");
        f[10] = Coalesce(o.EmployeeDesignation, "");
        f[11] = Coalesce(o.EmployeeBranch, "");
        f[12] = Coalesce(o.EmployeeCkycId, "");
        f[13] = Coalesce(o.InstitutionName, "");
        f[14] = Coalesce(o.InstitutionCode, "");
        f[15] = Doc(r.CustomerId, Coalesce(o.DeclarationDocument, ""));
        f[16] = Coalesce(o.DeclarationFlag, "Y");
        f[17] = Doc(r.CustomerId, Coalesce(o.ConsentDocument, ""));
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

    private static bool Is(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private string? Doc(string customerId, string? value) => string.IsNullOrEmpty(value) ? value : _documentName(customerId, value);
}
