using CKYC.Core.Domain;
using CKYC.Core.Models;

namespace CKYC.Core.Spec;

/// <summary>
/// Validates a single <see cref="LegalEntity"/> against the CERSAI CKYC legal-entity
/// file-format rules (the "File_Format_Upload_LegalEntity" Excel). Like the individual
/// validator it is a pre-flight gate for step 4 (build-zip): a record with any violation
/// is excluded from the batch instead of shipping a .UPL the FVU would reject.
/// </summary>
public sealed class LegalEntityRecordValidator
{
    private const string Record20 = "20";
    private const string Record30 = "30";
    private const string Record40 = "40";
    private const string Record50 = "50";
    private const string Record60 = "60";
    private const string Record70 = "70";

    private static bool Is(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsEmpty(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>Returns every rule violation for the record, or an empty list when it is valid.</summary>
    public static IReadOnlyList<ValidationError> Validate(LegalEntity le)
    {
        var errors = new List<ValidationError>();
        ValidateRecord20(le, errors);
        errors.AddRange(ValidateRecord30(le));
        errors.AddRange(ValidateRecord40(le));
        errors.AddRange(ValidateRecord50(le));
        errors.AddRange(ValidateRecord60(le));
        errors.AddRange(ValidateRecord70(le));
        return errors;
    }

    private static void ValidateRecord20(LegalEntity le, List<ValidationError> errors)
    {
        if (IsEmpty(le.EntityName))
            errors.Add(Error(Record20, "Entity Name", le.EntityName, "Entity Name is mandatory."));

        if (IsEmpty(le.EntityConstitution))
            errors.Add(Error(Record20, "Entity Constitution", le.EntityConstitution, "Entity Constitution is mandatory."));

        if (IsEmpty(le.DateOfIncorporation))
            errors.Add(Error(Record20, "Date of incorporation/Registration/Formation", le.DateOfIncorporation, "Date of incorporation/registration/formation is mandatory."));

        if (IsEmpty(le.CountryOfIncorporation))
            errors.Add(Error(Record20, "Country of incorporation", le.CountryOfIncorporation, "Country of incorporation is mandatory."));

        // PAN is mandatory for partnership firm, LLP and companies.
        if (le.EntityConstitution is LeConstitution.PartnershipFirm or LeConstitution.Llp or LeConstitution.PrivateLimitedCompany
            or LeConstitution.PublicLimitedCompany or LeConstitution.Section8Company)
        {
            if (IsEmpty(le.Pan))
                errors.Add(Error(Record20, "PAN", le.Pan, "PAN is mandatory for partnership firms, LLPs and companies."));
        }

        // One of PAN / Form 97 is provided otherwise.
        if (IsEmpty(le.Pan) && IsEmpty(le.Form97) && IsEmpty(le.TinGstNumber))
            errors.Add(Error(Record20, "PAN/Form 97/TIN", le.Pan ?? le.Form97 ?? le.TinGstNumber,
                "One of PAN, Form 97 (erstwhile Form 60) or TIN/GST registration number is required."));
    }

    private static IEnumerable<ValidationError> ValidateRecord30(LegalEntity le)
    {
        var proof = le.Proofs.FirstOrDefault();
        if (proof is null)
        {
            yield return Error(Record30, "Proof of Identity", null, "Record type 30 (Proof of Identity) is required.");
            yield break;
        }

        var con = le.EntityConstitution;
        if (LeConstitution.RequiresCin(con) && IsEmpty(proof.Cin))
            yield return Error(Record30, "CIN", proof.Cin, "CIN is mandatory for the selected constitution.");

        if (con is LeConstitution.PartnershipFirm or LeConstitution.Llp)
        {
            if (IsEmpty(proof.PartnershipDeed))
                yield return Error(Record30, "Partnership Deed", proof.PartnershipDeed, "Partnership Deed is mandatory for a partnership firm or LLP.");
            if (con is LeConstitution.Llp && IsEmpty(proof.Llpin))
                yield return Error(Record30, "LLPIN", proof.Llpin, "LLPIN is mandatory for an LLP.");
        }

        if (con is LeConstitution.Trust && IsEmpty(proof.TrustDeed))
            yield return Error(Record30, "Trust Deed", proof.TrustDeed, "Trust Deed is mandatory for a trust.");

        if (con is LeConstitution.UnincorporatedAssociation && IsEmpty(proof.ResolutionManagingBody))
            yield return Error(Record30, "Resolution of Managing Body", proof.ResolutionManagingBody,
                "Resolution of the managing body is mandatory for an unincorporated association.");

        if (!LeConstitution.IsCompany(con) && con is not LeConstitution.PartnershipFirm and not LeConstitution.Llp
            and not LeConstitution.Trust and not LeConstitution.UnincorporatedAssociation && con != LeConstitution.Others)
        {
            // Any other known constitution still needs a proof of identity document.
            if (IsEmpty(proof.SupportingDocumentsPoi) && IsEmpty(proof.OtherTypeRegistrationCertificate))
                yield return Error(Record30, "Supporting Documents for PoI", proof.SupportingDocumentsPoi,
                    "Supporting documents for proof of identity are required.");
        }
    }

    private static IEnumerable<ValidationError> ValidateRecord40(LegalEntity le)
    {
        if (le.RegisteredAddress is null)
        {
            yield return Error(Record40, "Registered office address", null, "Registered office address is mandatory.");
            yield break;
        }
        var reg = le.RegisteredAddress;
        if (IsEmpty(reg.Line1))
            yield return Error(Record40, "Registered Address Line 1", reg.Line1, "Registered address line 1 is mandatory.");
        if (IsEmpty(reg.ProofOfAddress))
            yield return Error(Record40, "Registered Proof of address", reg.ProofOfAddress, "Registered proof of address is mandatory.");
    }

    private static IEnumerable<ValidationError> ValidateRecord50(LegalEntity le)
    {
        if (le.Contact is null)
        {
            yield return Error(Record50, "Contact Details", null, "Record type 50 (Contact Details) is required.");
            yield break;
        }
        var c = le.Contact;
        if (IsEmpty(c.MobileNumber1))
            yield return Error(Record50, "Mobile number (01)", c.MobileNumber1, "Mobile number (01) is mandatory.");
        if (IsEmpty(c.Email1))
            yield return Error(Record50, "Email ID (01)", c.Email1, "Email ID (01) is mandatory.");
    }

    private static IEnumerable<ValidationError> ValidateRecord60(LegalEntity le)
    {
        var requiresRelated = LeConstitution.RequiresBeneficialOwner(le.EntityConstitution);
        if (le.RelatedParties.Count == 0)
        {
            if (requiresRelated)
                yield return Error(Record60, "Related Party Details", null, "At least one related party / beneficial owner is required.");
            yield break;
        }

        foreach (var rp in le.RelatedParties)
        {
            if (IsEmpty(rp.Relation))
                yield return Error(Record60, "Relation", rp.Relation, "Relation is mandatory.");
            if (IsEmpty(rp.CkycNumber))
                yield return Error(Record60, "CKYC Number", rp.CkycNumber, "CKYC Number of the related person is mandatory.");
            if (IsEmpty(rp.ControllingInterest))
                yield return Error(Record60, "Controlling interest", rp.ControllingInterest, "Controlling interest is mandatory.");
            if (Is(rp.ControllingInterest, "Ownership") && IsEmpty(rp.PercentageOwnership))
                yield return Error(Record60, "Percentage of Ownership/Exercise", rp.PercentageOwnership,
                    "Percentage of ownership is mandatory when controlling interest is 'Ownership'.");
            if (Is(rp.Relation, "Director") && IsEmpty(rp.Din))
                yield return Error(Record60, "DIN", rp.Din, "Director Identification Number is mandatory for a director.");
            if (Is(rp.Relation, "Others") && IsEmpty(rp.OtherRelationName))
                yield return Error(Record60, "Other Relation name", rp.OtherRelationName, "Other relation name is mandatory when relation is 'Others'.");
        }
    }

    private static IEnumerable<ValidationError> ValidateRecord70(LegalEntity le)
    {
        if (le.Other is null)
        {
            yield return Error(Record70, "Other Details & Attestation", null, "Record type 70 (Other Details & Attestation) is required.");
            yield break;
        }
        var o = le.Other;
        if (IsEmpty(o.AttestationDate))
            yield return Error(Record70, "Attestation date", o.AttestationDate, "Attestation date is mandatory.");
        if (IsEmpty(o.EmployeeName))
            yield return Error(Record70, "Employee Name", o.EmployeeName, "Employee name is mandatory.");
        if (IsEmpty(o.InstitutionName))
            yield return Error(Record70, "Institution Name", o.InstitutionName, "Institution name is mandatory.");
        if (IsEmpty(o.InstitutionCode))
            yield return Error(Record70, "Institution Code", o.InstitutionCode, "Institution code is mandatory.");
        if (IsEmpty(o.DeclarationDocument))
            yield return Error(Record70, "Declaration Document", o.DeclarationDocument, "Declaration document is mandatory.");
        if (IsEmpty(o.ConsentDocument))
            yield return Error(Record70, "Consent Document", o.ConsentDocument, "Consent document is mandatory.");
        if (IsEmpty(o.Place))
            yield return Error(Record70, "Place", o.Place, "Place is mandatory.");
        if (IsEmpty(o.DeclarationDate))
            yield return Error(Record70, "Declaration Date", o.DeclarationDate, "Declaration date is mandatory.");
    }

    private static ValidationError Error(string recordType, string field, string? value, string description)
        => new(null, recordType, null, field, value, null, description);
}
