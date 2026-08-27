using System.Globalization;
using System.Text.RegularExpressions;
using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Core.Spec;

/// <summary>Pre-flight validation for the legal-entity create workbook.</summary>
public sealed class LegalEntityRecordValidator
{
    private const string R20 = "20", R30 = "30", R40 = "40", R50 = "50", R60 = "60", R70 = "70";
    private static readonly Regex Pan = new(@"^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.CultureInvariant);
    private static readonly Regex Gst = new(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9]Z[A-Z0-9]$", RegexOptions.CultureInvariant);
    private static readonly Regex Cin = new(@"^[A-Z][0-9]{5}[A-Z]{2}[0-9]{4}[A-Z]{3}[0-9]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex Digits = new(@"^[0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"^[^\s@|]+@[^\s@|]+\.[^\s@|]+$", RegexOptions.CultureInvariant);
    private static readonly string[] Constitutions =
        ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R"];
    private static readonly string[] Relations =
        ["Director", "Promoter", "Karta", "Trustee", "Partner", "Court Appointment Official", "Proprietor",
         "Beneficiary", "Authorized Signatory", "Beneficial Owner", "Power of Attorney Holder", "Others"];

    /// <summary>
    /// Expected fourth PAN character per constitution code (FVU ERR_180 checks it, e.g. a
    /// trust's PAN must have T in the fourth position). Constitutions without a known
    /// character are not strictly checked.
    /// </summary>
    private static readonly Dictionary<string, char> ExpectedPanChars = new(StringComparer.OrdinalIgnoreCase)
    {
        [LeConstitution.SoleProprietorship] = 'P',       // individual/proprietor
        [LeConstitution.PartnershipFirm] = 'F',          // firm
        [LeConstitution.Llp] = 'F',
        [LeConstitution.Huf] = 'H',
        [LeConstitution.PrivateLimitedCompany] = 'C',
        [LeConstitution.PublicLimitedCompany] = 'C',
        [LeConstitution.Section8Company] = 'C',
        [LeConstitution.Trust] = 'T',
        [LeConstitution.UnincorporatedAssociation] = 'A',
    };

    public static IReadOnlyList<ValidationError> Validate(LegalEntity le)
    {
        var errors = new List<ValidationError>();
        Record20(le, errors); Record30(le, errors); Record40(le, errors);
        Record50(le, errors); Record60(le, errors); Record70(le, errors);
        return errors;
    }

    private static void Record20(LegalEntity le, List<ValidationError> e)
    {
        RequiredText(e, R20, "Search Key", le.SearchKey, 20);
        if (!Empty(le.SearchKey) && le.SearchKey.Length != 20) Add(e, R20, "Search Key", le.SearchKey, "Search Key must be exactly 20 characters.");
        RequiredText(e, R20, "Entity Name", le.EntityName, 99);
        RequiredAllowed(e, R20, "Entity Constitution", le.EntityConstitution, Constitutions);
        OptionalAllowed(e, R20, "Listed Company", le.ListedCompany, ["Y", "N"]);
        OptionalAllowed(e, R20, "Registered Firm", le.RegisteredFirm, ["Y", "N"]);
        OptionalAllowed(e, R20, "Registered Trust", le.RegisteredTrust, ["Y", "N"]);
        if (Is(le.EntityConstitution, LeConstitution.PublicLimitedCompany)) RequiredAllowed(e, R20, "Listed Company", le.ListedCompany, ["Y", "N"]);
        if (Is(le.EntityConstitution, LeConstitution.PartnershipFirm)) RequiredAllowed(e, R20, "Registered Firm", le.RegisteredFirm, ["Y", "N"]);
        if (Is(le.EntityConstitution, LeConstitution.Trust)) RequiredAllowed(e, R20, "Registered Trust", le.RegisteredTrust, ["Y", "N"]);

        RequiredDate(e, R20, "Date of incorporation/Registration/Formation", le.DateOfIncorporation, false);
        if (Is(le.EntityConstitution, LeConstitution.PublicLimitedCompany)) RequiredDate(e, R20, "Date of commencement of business", le.DateOfCommencement, false);
        else OptionalDate(e, R20, "Date of commencement of business", le.DateOfCommencement, false);
        RequiredText(e, R20, "Place of incorporation/Registration/Formation", le.PlaceOfIncorporation, 50);
        RequiredCode(e, R20, "Country of incorporation/Registration", le.CountryOfIncorporation, 2);
        OptionalCode(e, R20, "TIN issuing country", le.TinIssuingCountry, 2);

        var pan = le.Pan?.Trim().ToUpperInvariant();
        var panMandatory = le.EntityConstitution is LeConstitution.PartnershipFirm or LeConstitution.Llp
            or LeConstitution.PrivateLimitedCompany or LeConstitution.PublicLimitedCompany or LeConstitution.Section8Company;
        if (panMandatory && Empty(pan)) Add(e, R20, "PAN", le.Pan, "PAN is mandatory for partnership firms, LLPs and companies.");
        if (!Empty(pan) && !Pan.IsMatch(pan!)) Add(e, R20, "PAN", le.Pan, "PAN must match AAAAA9999A.");
        if (!Empty(pan) && ExpectedPanChars.TryGetValue(le.EntityConstitution?.Trim().ToUpperInvariant() ?? "", out var expectedChar) && pan![3] != expectedChar)
            Add(e, R20, "PAN", le.Pan, $"The fourth PAN character must be {expectedChar} for constitution type {le.EntityConstitution} (FVU ERR_180).");
        OptionalAllowed(e, R20, "Form 97", le.Form97, ["Y"]);
        if (Empty(pan) && !Is(le.Form97, "Y")) Add(e, R20, "Form 97", le.Form97, "Form 97 must be Y when PAN is not provided.");
        if (!Empty(pan)) RequiredAllowed(e, R20, "PAN Verified", le.PanVerified, ["Y", "N"]);
        else OptionalAllowed(e, R20, "PAN Verified", le.PanVerified, ["Y", "N"]);

        OptionalText(e, R20, "TIN/GST registration number", le.TinGstNumber, 15);
        if (!Empty(le.TinGstNumber) && !Gst.IsMatch(le.TinGstNumber!.Trim().ToUpperInvariant()))
            Add(e, R20, "TIN/GST registration number", le.TinGstNumber, "GST number must use the 15-character GST format.");
        if (!Empty(le.TinGstNumber)) RequiredDocument(e, R20, "TIN/GSTN document", le.TinGstnDocument);
        else OptionalDocument(e, R20, "TIN/GSTN document", le.TinGstnDocument);
        if (Empty(pan)) RequiredDocument(e, R20, "PAN/Form 97 document", le.PanDocument);
        else OptionalDocument(e, R20, "PAN document", le.PanDocument);
    }

    private static void Record30(LegalEntity le, List<ValidationError> e)
    {
        if (le.Proofs.Count != 1)
        {
            Add(e, R30, "Proof of Identity", le.Proofs.Count.ToString(), "Exactly one record type 30 is required for a legal entity.");
            if (le.Proofs.Count == 0) return;
        }
        var p = le.Proofs[0];
        if (LeConstitution.IsCompany(le.EntityConstitution))
        {
            RequiredDocument(e, R30, "Certificate of incorporation", p.CertificateOfIncorporation);
            RequiredText(e, R30, "CIN", p.Cin, 21);
            if (!Empty(p.Cin) && !Cin.IsMatch(p.Cin!.Trim().ToUpperInvariant())) Add(e, R30, "CIN", p.Cin, "CIN must use the 21-character company identification format.");
            RequiredDocument(e, R30, "Memorandum and articles of association", p.MemorandumAndArticles);
            RequiredDocument(e, R30, "Board resolution / Power of Attorney", p.ResolutionBoardPoA);
            RequiredDocument(e, R30, "Names of senior management", p.NamesSeniorManagement);
            if (Is(le.EntityConstitution, LeConstitution.PublicLimitedCompany)) RequiredDocument(e, R30, "Certificate of Commencement", p.CertificateOfCommencement);
            OptionalDocument(e, R30, "Other company document", p.OthersCompany);
        }
        else if (le.EntityConstitution is LeConstitution.PartnershipFirm or LeConstitution.Llp)
        {
            if (Is(le.RegisteredFirm, "Y")) { RequiredDocument(e, R30, "Registration Certificate", p.RegistrationCertificate); RequiredText(e, R30, "Registration Number", p.RegistrationNumber, 50); }
            if (Is(le.EntityConstitution, LeConstitution.Llp))
            {
                RequiredDocument(e, R30, "LLPIN Certificate", p.LlpinCertificate); RequiredText(e, R30, "LLPIN", p.Llpin, 7);
                if (!Empty(p.Llpin) && !AlphaNumeric(p.Llpin!, 7)) Add(e, R30, "LLPIN", p.Llpin, "LLPIN must contain exactly 7 alphanumeric characters.");
            }
            RequiredDocument(e, R30, "Partnership Deed", p.PartnershipDeed);
            RequiredDocument(e, R30, "Names of all partners", p.NamesAllPartners);
            OptionalDocument(e, R30, "Other partnership document", p.OthersPartnership);
        }
        else if (Is(le.EntityConstitution, LeConstitution.Trust))
        {
            if (Is(le.RegisteredTrust, "Y")) { RequiredDocument(e, R30, "Trust Registration Certificate", p.TrustRegistrationCertificate); RequiredText(e, R30, "Trust Registration Number", p.TrustRegistrationNumber, 50); }
            RequiredDocument(e, R30, "Trust Deed", p.TrustDeed);
            RequiredDocument(e, R30, "Names of beneficiaries/trustees/settlor", p.NamesBeneficiariesTrustees);
            RequiredDocument(e, R30, "Trust Power of Attorney", p.TrustPowerOfAttorney);
            OptionalDocument(e, R30, "Other trust document", p.OthersTrust);
        }
        else if (Is(le.EntityConstitution, LeConstitution.UnincorporatedAssociation))
        {
            OptionalDocument(e, R30, "Unincorporated Registration Certificate", p.UnincorporatedRegistrationCertificate);
            OptionalText(e, R30, "Unincorporated Registration Number", p.UnincorporatedRegistrationNumber, 50);
            RequiredDocument(e, R30, "Resolution of Managing Body", p.ResolutionManagingBody);
            RequiredDocument(e, R30, "Unincorporated Power of Attorney", p.UnincorporatedPowerOfAttorney);
            OptionalDocument(e, R30, "Information establishing existence", p.InfoEstablishExistence);
            OptionalDocument(e, R30, "Other unincorporated document", p.OthersUnincorporated);
        }
        else
        {
            RequiredDocument(e, R30, "Supporting Documents for PoI", p.SupportingDocumentsPoi);
            OptionalText(e, R30, "Other-type Registration Number", p.OtherTypeRegistrationNumber, 50);
            OptionalDocument(e, R30, "Other-type Registration Certificate", p.OtherTypeRegistrationCertificate);
            RequiredDocument(e, R30, "Other-type Power of Attorney", p.OtherTypePowerOfAttorney);
            OptionalDocument(e, R30, "Activity Proof 1", p.ActivityProof1);
            OptionalDocument(e, R30, "Activity Proof 2", p.ActivityProof2);
            OptionalDocument(e, R30, "Other constitution document", p.OthersOtherType);
        }
    }

    private static void Record40(LegalEntity le, List<ValidationError> e)
    {
        if (le.RegisteredAddress is null) { Add(e, R40, "Registered office address", null, "Registered office address is mandatory."); return; }
        Address(e, le.RegisteredAddress, "Registered");
        RequiredDocument(e, R40, "Document for registered address", le.RegisteredAddressDocument);
        var same = le.PrincipalAddress?.SameAsRegistered ?? (le.PrincipalAddress is null ? "Y" : null);
        RequiredAllowed(e, R40, "Same as registered address", same, ["Y", "N"]);
        if (Is(same, "N"))
        {
            if (le.PrincipalAddress is null) Add(e, R40, "Principal place of business", null, "Principal address is required when Same as registered address is N.");
            else { Address(e, le.PrincipalAddress, "Principal"); RequiredDocument(e, R40, "Document for principal place of business", le.PrincipalAddressDocument); }
        }
        else OptionalDocument(e, R40, "Document for principal place of business", le.PrincipalAddressDocument);
    }

    private static void Address(List<ValidationError> e, LeAddressDetails a, string section)
    {
        RequiredText(e, R40, $"{section} Address Line 1", a.Line1, 60);
        OptionalText(e, R40, $"{section} Address Line 2", a.Line2, 60); OptionalText(e, R40, $"{section} Address Line 3", a.Line3, 60);
        RequiredCode(e, R40, $"{section} Country", a.Country, 2);
        if (Is(a.Country, "IN"))
        {
            RequiredText(e, R40, $"{section} City", a.City, 60); RequiredCode(e, R40, $"{section} State", a.State, 2); RequiredText(e, R40, $"{section} District", a.District, 4);
            if (!Empty(a.District) && !Digits.IsMatch(a.District)) Add(e, R40, $"{section} District", a.District, "Indian district code must be numeric.");
            if (Empty(a.PinCode) || a.PinCode.Length != 6 || !Digits.IsMatch(a.PinCode)) Add(e, R40, $"{section} Pin Code", a.PinCode, "Indian pin code must contain exactly 6 digits.");
        }
        else { OptionalText(e, R40, $"{section} City", a.City, 60); OptionalCode(e, R40, $"{section} State", a.State, 2); OptionalCode(e, R40, $"{section} District", a.District, 2); OptionalText(e, R40, $"{section} Pin Code", a.PinCode, 6); }
        if (!Empty(a.PinCodeOthers) && (a.PinCodeOthers!.Length != 6 || !Digits.IsMatch(a.PinCodeOthers))) Add(e, R40, $"{section} Other Pin Code", a.PinCodeOthers, "Other pin code must contain exactly 6 digits.");
        OptionalText(e, R40, $"{section} DigiPIN", a.Digipin, 10);
        RequiredAllowed(e, R40, $"{section} Proof of Address", a.ProofOfAddress, ["A", "B", "C"]);
        if (Is(a.ProofOfAddress, "C")) RequiredText(e, R40, $"{section} Other Document Name", a.OtherDocumentName, 50); else OptionalText(e, R40, $"{section} Other Document Name", a.OtherDocumentName, 50);
    }

    private static void Record50(LegalEntity le, List<ValidationError> e)
    {
        if (le.Contact is null) { Add(e, R50, "Contact Details", null, "Record type 50 is mandatory."); return; }
        var c = le.Contact; RequiredText(e, R50, "Country code (01)", c.CountryCode1, 6);
        Mobile(e, "Mobile number (01)", c.CountryCode1, c.MobileNumber1, true); OptionalText(e, R50, "Country code (02)", c.CountryCode2, 6);
        Mobile(e, "Mobile number (02)", c.CountryCode2, c.MobileNumber2, false); Mail(e, "Email ID (01)", c.Email1, true); Mail(e, "Email ID (02)", c.Email2, false);
        OptionalText(e, R50, "Telephone", c.Telephone, 12); OptionalText(e, R50, "FAX", c.Fax, 12);
    }

    private static void Mobile(List<ValidationError> e, string field, string? code, string? value, bool required)
    {
        if (required && Empty(value)) { Add(e, R50, field, value, $"{field} is mandatory."); return; }
        if (Empty(value)) return;
        if (!Digits.IsMatch(value!) || value!.Length is < 8 or > 15) Add(e, R50, field, value, "Mobile number must contain 8 to 15 digits.");
        if (Is(code, "+91") && value!.Length != 10) Add(e, R50, field, value, "An Indian mobile number must contain exactly 10 digits.");
    }

    private static void Mail(List<ValidationError> e, string field, string? value, bool required)
    {
        if (required && Empty(value)) { Add(e, R50, field, value, $"{field} is mandatory."); return; }
        if (!Empty(value) && (value!.Length > 254 || !Email.IsMatch(value))) Add(e, R50, field, value, "Email must be valid and cannot exceed 254 characters.");
    }

    private static void Record60(LegalEntity le, List<ValidationError> e)
    {
        var needsOwner = LeConstitution.RequiresBeneficialOwner(le.EntityConstitution) && !Is(le.ListedCompany, "Y");
        if (needsOwner && !le.RelatedParties.Any(x => Is(x.Relation, "Beneficial Owner"))) Add(e, R60, "Beneficial Owner", null, "At least one Beneficial Owner is mandatory for this constitution.");
        foreach (var rp in le.RelatedParties)
        {
            RequiredAllowed(e, R60, "Relation", rp.Relation, Relations); RequiredText(e, R60, "CKYC Number", rp.CkycNumber, 14);
            if (!Empty(rp.CkycNumber) && (rp.CkycNumber.Length != 14 || !Digits.IsMatch(rp.CkycNumber))) Add(e, R60, "CKYC Number", rp.CkycNumber, "CKYC Number must contain exactly 14 digits.");
            RequiredAllowed(e, R60, "Controlling interest", rp.ControllingInterest, ["Ownership", "Through other means"]);
            if (Is(rp.ControllingInterest, "Ownership"))
            {
                RequiredText(e, R60, "Percentage of Ownership/Exercise", rp.PercentageOwnership, 10);
                if (!Empty(rp.PercentageOwnership) && (!decimal.TryParse(rp.PercentageOwnership, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct) || pct is < 0 or > 100)) Add(e, R60, "Percentage of Ownership/Exercise", rp.PercentageOwnership, "Percentage of ownership must be a number from 0 through 100.");
            }
            else OptionalText(e, R60, "Percentage of Ownership/Exercise", rp.PercentageOwnership, 10);
            if (Is(rp.Relation, "Others")) RequiredText(e, R60, "Other Relation name", rp.OtherRelationName, 33); else OptionalText(e, R60, "Other Relation name", rp.OtherRelationName, 33);
            if (Is(rp.Relation, "Director")) { RequiredText(e, R60, "DIN", rp.Din, 8); if (!Empty(rp.Din) && (rp.Din!.Length != 8 || !Digits.IsMatch(rp.Din))) Add(e, R60, "DIN", rp.Din, "DIN must contain exactly 8 digits."); }
            else OptionalText(e, R60, "DIN", rp.Din, 8);
        }
    }

    private static void Record70(LegalEntity le, List<ValidationError> e)
    {
        if (le.Other is null) { Add(e, R70, "Other Details & Attestation", null, "Record type 70 is mandatory."); return; }
        var o = le.Other; OptionalText(e, R70, "Remarks", o.Remarks, 200);
        RequiredAllowed(e, R70, "Certified copies", o.CertifiedCopies, ["Y", "N"]); RequiredAllowed(e, R70, "Equivalent e-document", o.EquivalentEDoc, ["Y", "N"]); RequiredAllowed(e, R70, "Verification from DigiLocker", o.VerificationFromDigiLocker, ["Y", "N"]);
        RequiredDate(e, R70, "Attestation date", o.AttestationDate, false); RequiredText(e, R70, "Employee Name", o.EmployeeName, 99); RequiredText(e, R70, "Employee Code", o.EmployeeCode, 50);
        RequiredText(e, R70, "Employee Designation", o.EmployeeDesignation, 50); RequiredText(e, R70, "Employee Branch", o.EmployeeBranch, 50); RequiredText(e, R70, "Employee CKYC ID", o.EmployeeCkycId, 14);
        if (!Empty(o.EmployeeCkycId) && (o.EmployeeCkycId.Length != 14 || !Digits.IsMatch(o.EmployeeCkycId))) Add(e, R70, "Employee CKYC ID", o.EmployeeCkycId, "Employee CKYC ID must contain exactly 14 digits.");
        RequiredText(e, R70, "Institution Name", o.InstitutionName, 99); RequiredText(e, R70, "Institution Code", o.InstitutionCode, 50);
        RequiredDocument(e, R70, "Declaration Document", o.DeclarationDocument); RequiredAllowed(e, R70, "Declaration Flag", o.DeclarationFlag, ["Y", "N"]); RequiredDocument(e, R70, "Consent Document", o.ConsentDocument);
        RequiredText(e, R70, "Place", o.Place, 40); RequiredDate(e, R70, "Declaration Date", o.DeclarationDate, false);
    }

    private static void RequiredDocument(List<ValidationError> e, string r, string f, string? v) { if (Empty(v)) Add(e, r, f, v, $"{f} is mandatory."); else Document(e, r, f, v!); }
    private static void OptionalDocument(List<ValidationError> e, string r, string f, string? v) { if (!Empty(v)) Document(e, r, f, v!); }
    private static void Document(List<ValidationError> e, string r, string f, string v)
    {
        OptionalText(e, r, f, v, 125); var ext = Path.GetExtension(v);
        if (Path.IsPathRooted(v) || !string.Equals(Path.GetFileName(v), v, StringComparison.Ordinal) || v is "." or "..") Add(e, r, f, v, "Document must be a file name without a directory path.");
        if (!new[] { ".pdf", ".jpg", ".jpeg" }.Contains(ext, StringComparer.OrdinalIgnoreCase)) Add(e, r, f, v, "Document must be a PDF, JPG or JPEG file.");
    }
    private static void RequiredText(List<ValidationError> e, string r, string f, string? v, int max) { if (Empty(v)) Add(e, r, f, v, $"{f} is mandatory."); else OptionalText(e, r, f, v, max); }
    private static void OptionalText(List<ValidationError> e, string r, string f, string? v, int max) { if (Empty(v)) return; if (v!.Length > max) Add(e, r, f, v, $"{f} cannot exceed {max} characters."); if (v.IndexOfAny(['|', '\r', '\n']) >= 0) Add(e, r, f, v, $"{f} cannot contain a pipe or line break."); }
    private static void RequiredCode(List<ValidationError> e, string r, string f, string? v, int len) { RequiredText(e, r, f, v, len); if (!Empty(v) && !AlphaNumeric(v!, len)) Add(e, r, f, v, $"{f} must contain exactly {len} alphanumeric characters."); }
    private static void OptionalCode(List<ValidationError> e, string r, string f, string? v, int len) { if (!Empty(v) && !AlphaNumeric(v!, len)) Add(e, r, f, v, $"{f} must contain exactly {len} alphanumeric characters."); }
    private static void RequiredAllowed(List<ValidationError> e, string r, string f, string? v, string[] allowed) { if (Empty(v)) Add(e, r, f, v, $"{f} is mandatory."); else if (!allowed.Any(x => Is(v, x))) Add(e, r, f, v, $"{f} must be one of: {string.Join(", ", allowed)}."); }
    private static void OptionalAllowed(List<ValidationError> e, string r, string f, string? v, string[] allowed) { if (!Empty(v) && !allowed.Any(x => Is(v, x))) Add(e, r, f, v, $"{f} must be one of: {string.Join(", ", allowed)}."); }
    private static void RequiredDate(List<ValidationError> e, string r, string f, string? v, bool compact) { if (Empty(v)) Add(e, r, f, v, $"{f} is mandatory."); else OptionalDate(e, r, f, v, compact); }
    private static void OptionalDate(List<ValidationError> e, string r, string f, string? v, bool compact) { if (Empty(v)) return; var format = compact ? "ddMMyyyy" : "dd-MM-yyyy"; if (!DateOnly.TryParseExact(v, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) Add(e, r, f, v, $"{f} must be a valid date in {format.ToUpperInvariant()} format."); }
    private static bool AlphaNumeric(string value, int length) => value.Length == length && value.All(char.IsLetterOrDigit);
    private static bool Empty(string? value) => string.IsNullOrWhiteSpace(value);
    private static bool Is(string? value, string expected) => string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    private static void Add(List<ValidationError> e, string r, string f, string? v, string d) => e.Add(new ValidationError(null, r, null, f, v, null, d));
}
