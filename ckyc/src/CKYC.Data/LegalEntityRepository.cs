using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using CKYC.Core.Spec;
using Microsoft.EntityFrameworkCore;
using LegalEntityRecord20Entity = CKYC.Data.Entities.LegalEntityRecord20;
using LegalEntityRecord30Entity = CKYC.Data.Entities.LegalEntityRecord30;
using LegalEntityRecord40Entity = CKYC.Data.Entities.LegalEntityRecord40;
using LegalEntityRecord50Entity = CKYC.Data.Entities.LegalEntityRecord50;
using LegalEntityRecord60Entity = CKYC.Data.Entities.LegalEntityRecord60;
using LegalEntityRecord70Entity = CKYC.Data.Entities.LegalEntityRecord70;

namespace CKYC.Data;

/// <summary>EF Core (SQL Server) persistence for the legal-entity record tables (20–70).</summary>
public sealed class LegalEntityRepository : ILegalEntityRepository
{
    private readonly ICkycDatabase _db;

    public LegalEntityRepository(ICkycDatabase db) => _db = db;

    public async Task<SaveRecordResult> SaveAsync(LegalEntity record, CancellationToken ct = default)
    {
        if (record.MasterRecordId <= 0)
            return new SaveRecordResult(record.MasterRecordId, false, "MasterRecordId is required", null);

        var validationErrors = LegalEntityRecordValidator.Validate(record);
        if (validationErrors.Count > 0)
            return new SaveRecordResult(record.MasterRecordId, false,
                string.Join("; ", validationErrors.Select(e => $"[{e.RecordType}/{e.FieldName}] {e.ErrorDescription}")), null);

        try
        {
            await using var db = _db.CreateContext();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await DeleteExistingAsync(db, record.MasterRecordId, ct);

            var now = DateTime.UtcNow;
            InsertRecord20(db, record, now);
            InsertRecord30(db, record.MasterRecordId, record.CustomerId, FirstProof(record));
            InsertRecord40(db, record);
            InsertRecord50(db, record);
            foreach (var rp in record.RelatedParties)
                InsertRecord60(db, record, rp);
            InsertRecord70(db, record);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new SaveRecordResult(record.MasterRecordId, true, null,
                $"Saved entity details + POI, addresses, contact, {record.RelatedParties.Count} related party(ies), attestation");
        }
        catch (Exception ex)
        {
            return new SaveRecordResult(record.MasterRecordId, false, ex.Message, null);
        }
    }

    public async Task<IReadOnlyList<LegalEntity>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return Array.Empty<LegalEntity>();
        await using var db = _db.CreateContext();

        var r20s = await db.LegalEntityRecord20s.AsNoTracking()
            .Where(r => customerIds.Contains(r.CustomerId!))
            .ToListAsync(ct);
        if (r20s.Count == 0) return Array.Empty<LegalEntity>();

        var masterIds = r20s.Select(r => r.MasterRecordId ?? 0).Where(id => id > 0).ToList();

        var r30s = await db.LegalEntityRecord30s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r40s = await db.LegalEntityRecord40s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r50s = await db.LegalEntityRecord50s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r60s = await db.LegalEntityRecord60s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r70s = await db.LegalEntityRecord70s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);

        var r30ByMaster = r30s.ToLookup(r => r.MasterRecordId ?? 0);
        var r40ByMaster = r40s.ToLookup(r => r.MasterRecordId ?? 0);
        var r50ByMaster = r50s.ToLookup(r => r.MasterRecordId ?? 0);
        var r60ByMaster = r60s.ToLookup(r => r.MasterRecordId ?? 0);
        var r70ByMaster = r70s.ToLookup(r => r.MasterRecordId ?? 0);

        var result = new List<LegalEntity>(r20s.Count);
        foreach (var r20 in r20s)
        {
            var masterId = r20.MasterRecordId ?? 0;
            var le = ReadRecord20(r20);
            le.MasterRecordId = masterId;
            le.CustomerId = r20.CustomerId ?? string.Empty;
            le.Proofs = r30ByMaster[masterId].Select(ReadRecord30).ToList();
            le.RelatedParties = r60ByMaster[masterId].Select(ReadRecord60).ToList();

            var r40 = r40ByMaster[masterId].FirstOrDefault();
            if (r40 is not null)
            {
                le.RegisteredAddress = ToAddress(r40, "Reg");
                le.PrincipalAddress = ToAddress(r40, "Prin");
                le.RegisteredAddressDocument = r40.RegDocument;
                le.PrincipalAddressDocument = r40.PrinDocument;
            }
            le.Contact = r50ByMaster[masterId].Select(ReadRecord50).FirstOrDefault();
            le.Other = r70ByMaster[masterId].Select(ReadRecord70).FirstOrDefault();
            result.Add(le);
        }
        return result;
    }

    private static LeProofOfIdentity ReadRecord30(LegalEntityRecord30Entity r) => new()
    {
        CertificateOfIncorporation = r.CertificateOfIncorporation, Cin = r.Cin,
        MemorandumAndArticles = r.MemorandumAndArticles, ResolutionBoardPoA = r.ResolutionBoardPoA,
        NamesSeniorManagement = r.NamesSeniorManagement, CertificateOfCommencement = r.CertificateOfCommencement,
        OthersCompany = r.OthersCompany,
        RegistrationCertificate = r.RegistrationCertificate, RegistrationNumber = r.RegistrationNumber,
        LlpinCertificate = r.LlpinCertificate, Llpin = r.Llpin, PartnershipDeed = r.PartnershipDeed,
        NamesAllPartners = r.NamesAllPartners, OthersPartnership = r.OthersPartnership,
        TrustRegistrationCertificate = r.TrustRegistrationCertificate, TrustRegistrationNumber = r.TrustRegistrationNumber,
        TrustDeed = r.TrustDeed, NamesBeneficiariesTrustees = r.NamesBeneficiariesTrustees,
        TrustPowerOfAttorney = r.TrustPowerOfAttorney, OthersTrust = r.OthersTrust,
        UnincorporatedRegistrationCertificate = r.UnincorporatedRegCertificate, UnincorporatedRegistrationNumber = r.UnincorporatedRegNumber,
        ResolutionManagingBody = r.ResolutionManagingBody, UnincorporatedPowerOfAttorney = r.UnincorporatedPowerOfAttorney,
        InfoEstablishExistence = r.InfoEstablishExistence, OthersUnincorporated = r.OthersUnincorporated,
        SupportingDocumentsPoi = r.SupportingDocumentsPoi, OtherTypeRegistrationNumber = r.OtherTypeRegistrationNumber,
        OtherTypeRegistrationCertificate = r.OtherTypeRegistrationCertificate, OtherTypePowerOfAttorney = r.OtherTypePowerOfAttorney,
        ActivityProof1 = r.ActivityProof1, ActivityProof2 = r.ActivityProof2, OthersOtherType = r.OthersOtherType,
    };

    private static LeAddressDetails ToAddress(LegalEntityRecord40Entity r, string pfx)
    {
        string? G(string c) => c switch
        {
            "RegLine1" => r.RegLine1, "RegLine2" => r.RegLine2, "RegLine3" => r.RegLine3,
            "RegCity" => r.RegCity, "RegState" => r.RegState, "RegDistrict" => r.RegDistrict,
            "RegPinCode" => r.RegPinCode, "RegPinOthers" => r.RegPinOthers, "RegDigipin" => r.RegDigipin,
            "RegCountry" => r.RegCountry, "RegProofOfAddress" => r.RegProofOfAddress,
            "RegOtherDocumentName" => r.RegOtherDocumentName,
            "PrinLine1" => r.PrinLine1, "PrinLine2" => r.PrinLine2, "PrinLine3" => r.PrinLine3,
            "PrinCity" => r.PrinCity, "PrinState" => r.PrinState, "PrinDistrict" => r.PrinDistrict,
            "PrinPinCode" => r.PrinPinCode, "PrinPinOthers" => r.PrinPinOthers, "PrinDigipin" => r.PrinDigipin,
            "PrinCountry" => r.PrinCountry, "PrinProofOfAddress" => r.PrinProofOfAddress,
            "PrinOtherDocumentName" => r.PrinOtherDocumentName,
            _ => null,
        };
        return new LeAddressDetails
        {
            Line1 = G($"{pfx}Line1") ?? "", Line2 = G($"{pfx}Line2") ?? "", Line3 = G($"{pfx}Line3") ?? "",
            City = G($"{pfx}City") ?? "", State = G($"{pfx}State") ?? "", District = G($"{pfx}District") ?? "",
            PinCode = G($"{pfx}PinCode") ?? "", PinCodeOthers = G($"{pfx}PinOthers"), Digipin = G($"{pfx}Digipin"),
            Country = G($"{pfx}Country") ?? "IN", ProofOfAddress = G($"{pfx}ProofOfAddress") ?? "A",
            OtherDocumentName = G($"{pfx}OtherDocumentName"),
            SameAsRegistered = pfx == "Prin" ? r.SameAsRegistered : null,
        };
    }

    private static LeContactDetails ReadRecord50(LegalEntityRecord50Entity r) => new()
    {
        CountryCode1 = r.CountryCode1 ?? "+91", MobileNumber1 = r.MobileNumber1,
        CountryCode2 = r.CountryCode2 ?? "+91", MobileNumber2 = r.MobileNumber2,
        Email1 = r.EmailId1, Email2 = r.EmailId2, Telephone = r.Telephone, Fax = r.Fax,
    };

    private static LeRelatedParty ReadRecord60(LegalEntityRecord60Entity r) => new()
    {
        Relation = r.Relation ?? "", CkycNumber = r.CkycNumber ?? "",
        ControllingInterest = r.ControllingInterest ?? "",
        PercentageOwnership = r.PercentageOwnership, OtherRelationName = r.OtherRelationName,
        Din = r.Din,
    };

    private static LeOtherDetails ReadRecord70(LegalEntityRecord70Entity r) => new()
    {
        Remarks = r.Remarks, CertifiedCopies = r.CertifiedCopies ?? "Y", EquivalentEDoc = r.EquivalentEdoc ?? "N",
        VerificationFromDigiLocker = r.VerificationFromDigiLocker ?? "N", AttestationDate = r.AttestationDate ?? "",
        EmployeeName = r.EmployeeName ?? "", EmployeeCode = r.EmployeeCode ?? "",
        EmployeeDesignation = r.EmployeeDesignation ?? "", EmployeeBranch = r.EmployeeBranch ?? "",
        EmployeeCkycId = r.EmployeeCkycId ?? "", InstitutionName = r.InstitutionName ?? "",
        InstitutionCode = r.InstitutionCode ?? "", DeclarationDocument = r.DeclarationDocument ?? "",
        DeclarationFlag = r.DeclarationFlag ?? "Y", ConsentDocument = r.ConsentDocument ?? "",
        Place = r.Place ?? "", DeclarationDate = r.DeclarationDate ?? "",
    };

    private static void InsertRecord20(CkycDbContext db, LegalEntity r, DateTime now)
    {
        db.LegalEntityRecord20s.Add(new LegalEntityRecord20Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId,
            SearchKey = r.SearchKey, EntityName = r.EntityName, EntityConstitution = r.EntityConstitution,
            ListedCompany = r.ListedCompany, RegisteredFirm = r.RegisteredFirm, RegisteredTrust = r.RegisteredTrust,
            DateOfIncorporation = r.DateOfIncorporation, DateOfCommencement = r.DateOfCommencement,
            PlaceOfIncorporation = r.PlaceOfIncorporation, CountryOfIncorporation = r.CountryOfIncorporation,
            TinIssuingCountry = r.TinIssuingCountry, Pan = r.Pan, Form97 = r.Form97,
            TinGstNumber = r.TinGstNumber, PanDocument = r.PanDocument, PanVerified = r.PanVerified,
            TinGstnDocument = r.TinGstnDocument,
            CreatedAt = now, UpdatedAt = now,
        });
    }

    private static void InsertRecord30(CkycDbContext db, long masterId, string customerId, LeProofOfIdentity? p)
    {
        db.LegalEntityRecord30s.Add(new LegalEntityRecord30Entity
        {
            MasterRecordId = masterId, CustomerId = customerId, Record20LineNumber = 1,
            CertificateOfIncorporation = p?.CertificateOfIncorporation, Cin = p?.Cin,
            MemorandumAndArticles = p?.MemorandumAndArticles, ResolutionBoardPoA = p?.ResolutionBoardPoA,
            NamesSeniorManagement = p?.NamesSeniorManagement, CertificateOfCommencement = p?.CertificateOfCommencement,
            OthersCompany = p?.OthersCompany,
            RegistrationCertificate = p?.RegistrationCertificate, RegistrationNumber = p?.RegistrationNumber,
            LlpinCertificate = p?.LlpinCertificate, Llpin = p?.Llpin, PartnershipDeed = p?.PartnershipDeed,
            NamesAllPartners = p?.NamesAllPartners, OthersPartnership = p?.OthersPartnership,
            TrustRegistrationCertificate = p?.TrustRegistrationCertificate, TrustRegistrationNumber = p?.TrustRegistrationNumber,
            TrustDeed = p?.TrustDeed, NamesBeneficiariesTrustees = p?.NamesBeneficiariesTrustees,
            TrustPowerOfAttorney = p?.TrustPowerOfAttorney, OthersTrust = p?.OthersTrust,
            UnincorporatedRegCertificate = p?.UnincorporatedRegistrationCertificate,
            UnincorporatedRegNumber = p?.UnincorporatedRegistrationNumber,
            ResolutionManagingBody = p?.ResolutionManagingBody, UnincorporatedPowerOfAttorney = p?.UnincorporatedPowerOfAttorney,
            InfoEstablishExistence = p?.InfoEstablishExistence, OthersUnincorporated = p?.OthersUnincorporated,
            SupportingDocumentsPoi = p?.SupportingDocumentsPoi,
            OtherTypeRegistrationNumber = p?.OtherTypeRegistrationNumber,
            OtherTypeRegistrationCertificate = p?.OtherTypeRegistrationCertificate,
            OtherTypePowerOfAttorney = p?.OtherTypePowerOfAttorney,
            ActivityProof1 = p?.ActivityProof1, ActivityProof2 = p?.ActivityProof2, OthersOtherType = p?.OthersOtherType,
        });
    }

    private static void InsertRecord40(CkycDbContext db, LegalEntity r)
    {
        var reg = r.RegisteredAddress;
        var prin = r.PrincipalAddress;
        db.LegalEntityRecord40s.Add(new LegalEntityRecord40Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId, Record20LineNumber = 1,
            RegLine1 = reg?.Line1, RegLine2 = reg?.Line2, RegLine3 = reg?.Line3,
            RegCity = reg?.City, RegState = reg?.State, RegDistrict = reg?.District,
            RegPinCode = reg?.PinCode, RegPinOthers = reg?.PinCodeOthers, RegDigipin = reg?.Digipin,
            RegCountry = reg?.Country, RegProofOfAddress = reg?.ProofOfAddress, RegOtherDocumentName = reg?.OtherDocumentName,
            RegDocument = r.RegisteredAddressDocument,
            SameAsRegistered = prin?.SameAsRegistered ?? (prin is null ? "Y" : "N"),
            PrinLine1 = prin?.Line1, PrinLine2 = prin?.Line2, PrinLine3 = prin?.Line3,
            PrinCity = prin?.City, PrinState = prin?.State, PrinDistrict = prin?.District,
            PrinPinCode = prin?.PinCode, PrinPinOthers = prin?.PinCodeOthers, PrinDigipin = prin?.Digipin,
            PrinCountry = prin?.Country, PrinProofOfAddress = prin?.ProofOfAddress, PrinOtherDocumentName = prin?.OtherDocumentName,
            PrinDocument = r.PrincipalAddressDocument,
        });
    }

    private static void InsertRecord50(CkycDbContext db, LegalEntity r)
    {
        var c = r.Contact;
        db.LegalEntityRecord50s.Add(new LegalEntityRecord50Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId, Record20LineNumber = 1,
            CountryCode1 = c?.CountryCode1, MobileNumber1 = c?.MobileNumber1,
            CountryCode2 = c?.CountryCode2, MobileNumber2 = c?.MobileNumber2,
            EmailId1 = c?.Email1, EmailId2 = c?.Email2, Telephone = c?.Telephone, Fax = c?.Fax,
        });
    }

    private static void InsertRecord60(CkycDbContext db, LegalEntity record, LeRelatedParty rp)
    {
        db.LegalEntityRecord60s.Add(new LegalEntityRecord60Entity
        {
            MasterRecordId = record.MasterRecordId, CustomerId = record.CustomerId, Record20LineNumber = 1,
            NumberOfRelatedPersons = record.RelatedParties.Count,
            NumberOfBeneficialOwners = record.RelatedParties.Count(x => string.Equals(x.Relation?.Trim(), "Beneficial Owner", StringComparison.OrdinalIgnoreCase)),
            Relation = rp.Relation, CkycNumber = rp.CkycNumber,
            ControllingInterest = rp.ControllingInterest, PercentageOwnership = rp.PercentageOwnership,
            OtherRelationName = rp.OtherRelationName, Din = rp.Din,
        });
    }

    private static void InsertRecord70(CkycDbContext db, LegalEntity r)
    {
        var o = r.Other;
        db.LegalEntityRecord70s.Add(new LegalEntityRecord70Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId, Record20LineNumber = 1,
            Remarks = o?.Remarks, CertifiedCopies = o?.CertifiedCopies,
            EquivalentEdoc = o?.EquivalentEDoc, VerificationFromDigiLocker = o?.VerificationFromDigiLocker,
            AttestationDate = o?.AttestationDate, EmployeeName = o?.EmployeeName, EmployeeCode = o?.EmployeeCode,
            EmployeeDesignation = o?.EmployeeDesignation, EmployeeBranch = o?.EmployeeBranch, EmployeeCkycId = o?.EmployeeCkycId,
            InstitutionName = o?.InstitutionName, InstitutionCode = o?.InstitutionCode,
            DeclarationDocument = o?.DeclarationDocument, DeclarationFlag = o?.DeclarationFlag,
            ConsentDocument = o?.ConsentDocument, Place = o?.Place, DeclarationDate = o?.DeclarationDate,
        });
    }

    private static async Task DeleteExistingAsync(CkycDbContext db, long masterId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM legal_entity_record_20 WHERE MasterRecordId={{masterId}};
            DELETE FROM legal_entity_record_30 WHERE MasterRecordId={{masterId}};
            DELETE FROM legal_entity_record_40 WHERE MasterRecordId={{masterId}};
            DELETE FROM legal_entity_record_50 WHERE MasterRecordId={{masterId}};
            DELETE FROM legal_entity_record_60 WHERE MasterRecordId={{masterId}};
            DELETE FROM legal_entity_record_70 WHERE MasterRecordId={{masterId}};
            """, ct);
    }

    private static LeProofOfIdentity FirstProof(LegalEntity le)
        => le.Proofs.FirstOrDefault() ?? new LeProofOfIdentity();

    private static LegalEntity ReadRecord20(LegalEntityRecord20Entity r) => new()
    {
        Id = r.Id,
        SearchKey = r.SearchKey ?? "",
        EntityName = r.EntityName ?? "",
        EntityConstitution = r.EntityConstitution ?? "",
        ListedCompany = r.ListedCompany, RegisteredFirm = r.RegisteredFirm, RegisteredTrust = r.RegisteredTrust,
        DateOfIncorporation = r.DateOfIncorporation, DateOfCommencement = r.DateOfCommencement,
        PlaceOfIncorporation = r.PlaceOfIncorporation, CountryOfIncorporation = r.CountryOfIncorporation,
        TinIssuingCountry = r.TinIssuingCountry, Pan = r.Pan, Form97 = r.Form97,
        TinGstNumber = r.TinGstNumber, PanDocument = r.PanDocument, PanVerified = r.PanVerified,
        TinGstnDocument = r.TinGstnDocument,
    };
}
