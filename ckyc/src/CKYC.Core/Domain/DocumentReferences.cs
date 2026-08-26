namespace CKYC.Core.Domain;

/// <summary>Enumerates the filenames referenced by CKYC record fields.</summary>
public static class DocumentReferences
{
    public static IReadOnlySet<string> For(Individual record)
    {
        var files = NewSet();
        Add(files, record.PhotoOfIndividual);
        Add(files, record.PanDocument);
        foreach (var proof in record.Proofs) Add(files, proof.CopyOfOvd);
        Add(files, record.PermanentAddress?.CopyOfOvd);
        Add(files, record.CurrentAddress?.CopyOfOvd);
        Add(files, record.Other?.DeclarationDocument);
        Add(files, record.Other?.ClientConsent);
        return files;
    }

    public static IReadOnlySet<string> For(LegalEntity record)
    {
        var files = NewSet();
        Add(files, record.PanDocument); Add(files, record.TinGstnDocument);
        Add(files, record.RegisteredAddressDocument); Add(files, record.PrincipalAddressDocument);
        foreach (var p in record.Proofs)
        {
            Add(files, p.CertificateOfIncorporation); Add(files, p.MemorandumAndArticles);
            Add(files, p.ResolutionBoardPoA); Add(files, p.NamesSeniorManagement);
            Add(files, p.CertificateOfCommencement); Add(files, p.OthersCompany);
            Add(files, p.RegistrationCertificate); Add(files, p.LlpinCertificate);
            Add(files, p.PartnershipDeed); Add(files, p.NamesAllPartners); Add(files, p.OthersPartnership);
            Add(files, p.TrustRegistrationCertificate); Add(files, p.TrustDeed);
            Add(files, p.NamesBeneficiariesTrustees); Add(files, p.TrustPowerOfAttorney); Add(files, p.OthersTrust);
            Add(files, p.UnincorporatedRegistrationCertificate); Add(files, p.ResolutionManagingBody);
            Add(files, p.UnincorporatedPowerOfAttorney); Add(files, p.InfoEstablishExistence); Add(files, p.OthersUnincorporated);
            Add(files, p.SupportingDocumentsPoi); Add(files, p.OtherTypeRegistrationCertificate);
            Add(files, p.OtherTypePowerOfAttorney); Add(files, p.ActivityProof1); Add(files, p.ActivityProof2); Add(files, p.OthersOtherType);
        }
        Add(files, record.Other?.DeclarationDocument); Add(files, record.Other?.ConsentDocument);
        return files;
    }

    private static HashSet<string> NewSet() => new(StringComparer.OrdinalIgnoreCase);
    private static void Add(HashSet<string> files, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) files.Add(value.Trim());
    }
}

