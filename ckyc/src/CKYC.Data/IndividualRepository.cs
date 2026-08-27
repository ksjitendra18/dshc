using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using Microsoft.EntityFrameworkCore;
using IndividualRecord20Entity = CKYC.Data.Entities.IndividualRecord20;
using IndividualRecord30Entity = CKYC.Data.Entities.IndividualRecord30;
using IndividualRecord40Entity = CKYC.Data.Entities.IndividualRecord40;
using IndividualRecord50Entity = CKYC.Data.Entities.IndividualRecord50;
using IndividualRecord60Entity = CKYC.Data.Entities.IndividualRecord60;
using IndividualRecord70Entity = CKYC.Data.Entities.IndividualRecord70;

namespace CKYC.Data;

/// <summary>EF Core (SQL Server) persistence for the individual record tables (20–70).</summary>
public sealed class IndividualRepository : IIndividualRepository
{
    private readonly ICkycDatabase _db;

    public IndividualRepository(ICkycDatabase db) => _db = db;

    public async Task<SaveRecordResult> SaveAsync(Individual record, CancellationToken ct = default)
    {
        if (record.MasterRecordId <= 0)
            return new SaveRecordResult(record.MasterRecordId, false, "MasterRecordId is required", null);

        try
        {
            await using var db = _db.CreateContext();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await DeleteExistingAsync(db, record.MasterRecordId, ct);

            var now = DateTime.UtcNow;
            InsertRecord20(db, record, now);
            foreach (var proof in record.Proofs) InsertRecord30(db, record.MasterRecordId, record.CustomerId, proof, now);
            InsertRecord40(db, record);
            InsertRecord50(db, record);
            foreach (var rp in record.RelatedParties) InsertRecord60(db, record.MasterRecordId, record.CustomerId, rp);
            InsertRecord70(db, record);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new SaveRecordResult(record.MasterRecordId, true, null,
                $"Saved demographics + {record.Proofs.Count} proof(s), addresses, contact, " +
                $"{record.RelatedParties.Count} related party(ies), attestation");
        }
        catch (Exception ex)
        {
            var message = ex is DbUpdateException && ex.InnerException is { } inner ? inner.Message : ex.Message;
            return new SaveRecordResult(record.MasterRecordId, false, message, null);
        }
    }

    public async Task<IReadOnlyList<Individual>> GetByCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return Array.Empty<Individual>();
        await using var db = _db.CreateContext();

        var r20s = await db.IndividualRecord20s.AsNoTracking()
            .Where(r => customerIds.Contains(r.CustomerId!))
            .ToListAsync(ct);
        if (r20s.Count == 0) return Array.Empty<Individual>();

        var masterIds = r20s.Select(r => r.MasterRecordId ?? 0).Where(id => id > 0).ToList();

        var r30s = await db.IndividualRecord30s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r40s = await db.IndividualRecord40s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r50s = await db.IndividualRecord50s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r60s = await db.IndividualRecord60s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);
        var r70s = await db.IndividualRecord70s.AsNoTracking()
            .Where(r => masterIds.Contains(r.MasterRecordId ?? 0)).ToListAsync(ct);

        // Build the relationships once. Re-filtering every child list for every customer is
        // quadratic at the 500-record individual batch limit.
        var r30ByMaster = r30s.ToLookup(r => r.MasterRecordId ?? 0);
        var r40ByMaster = r40s.ToLookup(r => r.MasterRecordId ?? 0);
        var r50ByMaster = r50s.ToLookup(r => r.MasterRecordId ?? 0);
        var r60ByMaster = r60s.ToLookup(r => r.MasterRecordId ?? 0);
        var r70ByMaster = r70s.ToLookup(r => r.MasterRecordId ?? 0);

        var result = new List<Individual>(r20s.Count);
        foreach (var r20 in r20s)
        {
            var masterId = r20.MasterRecordId ?? 0;
            var ind = ReadRecord20(r20);
            ind.MasterRecordId = masterId;
            ind.CustomerId = r20.CustomerId ?? string.Empty;
            ind.Proofs = r30ByMaster[masterId].Select(ReadRecord30).ToList();
            ind.RelatedParties = r60ByMaster[masterId].Select(ReadRecord60).ToList();

            var r40 = r40ByMaster[masterId].FirstOrDefault();
            ind.PermanentAddress = r40 is null ? null : ReadAddress(r40, "Perm");
            var currentAddress = r40 is null ? null : ReadAddress(r40, "Curr");
            ind.CurrentAddressSameAsPermanent = r40?.CurrSameAsPermanent;
            ind.CurrentAddress = currentAddress;
            ind.Contact = r50ByMaster[masterId].Select(ReadRecord50).FirstOrDefault();
            ind.Other = r70ByMaster[masterId].Select(ReadRecord70).FirstOrDefault();
            result.Add(ind);
        }
        return result;
    }

    private static Individual ReadRecord20(IndividualRecord20Entity r) => new()
    {
        Id = r.Id,
        SearchKey = r.SearchKey ?? "",
        KycType = r.KycType ?? "",
        Name = new PersonName { Title = r.NameTitle ?? "", FirstName = r.NameFirst ?? "", MiddleName = r.NameMiddle ?? "", LastName = r.NameLast ?? "" },
        MaidenName = new PersonName { Title = r.MaidenTitle ?? "", FirstName = r.MaidenFirst ?? "", MiddleName = r.MaidenMiddle ?? "", LastName = r.MaidenLast ?? "" },
        MotherName = new PersonName { Title = r.MotherTitle ?? "", FirstName = r.MotherFirst ?? "", MiddleName = r.MotherMiddle ?? "", LastName = r.MotherLast ?? "" },
        FatherName = new PersonName { Title = r.FatherTitle ?? "", FirstName = r.FatherFirst ?? "", MiddleName = r.FatherMiddle ?? "", LastName = r.FatherLast ?? "" },
        SpouseName = new PersonName { Title = r.SpouseTitle ?? "", FirstName = r.SpouseFirst ?? "", MiddleName = r.SpouseMiddle ?? "", LastName = r.SpouseLast ?? "" },
        DateOfBirth = r.DateOfBirth,
        Gender = r.Gender,
        ResidentialStatus = r.ResidentialStatus,
        ResidentialStatusSupportedByDocument = r.ResidentialSupportedByDocument,
        Nationality = r.Nationality,
        NationalitySupportedByDocument = r.NationalitySupportedByDocument,
        DifferentlyAbledStatus = r.DifferentlyAbledStatus,
        DifferentlyAbledType = r.DifferentlyAbledType,
        Pan = r.Pan,
        PanVerified = r.PanVerified,
        PhotoOfIndividual = r.PhotoOfIndividual,
        Minor = r.Minor,
        DateOfBirthMatchWithOvd = r.DoBmatchWithOvd,
        NameMatchWithOvd = r.NameMatchWithOvd,
        PhotoProvidedMatchWithOvd = r.PhotoMatchWithOvd,
        GenderProvidedInOvd = r.GenderProvidedInOvd,
        GenderMatchWithOvd = r.GenderMatchWithOvd,
        Form97Provided = r.Form97Provided,
        Form61Provided = r.Form61Provided,
        PanDocument = r.PanDocument,
        OtherTypeOfImpairment = r.OtherTypeOfImpairment,
        DisabilityReferenceNumber = r.DisabilityReferenceNumber,
        PermanentDisability = r.PermanentDisability,
        DisabilityDate = r.DisabilityDate,
        PercentageOfImpairment = r.PercentageOfImpairment,
        DifferentlyAbledSupportedByDocument = r.DifferentlyAbledSupportedByDocument,
    };

    private static ProofOfIdentity ReadRecord30(IndividualRecord30Entity r) => new()
    {
        OvdType = r.OvdType ?? "", ModeOfAadhaarVerification = r.ModeOfAadhaarVerification ?? "",
        PassportExpiryDate = r.PassportExpiryDate, DrivingLicenseExpiryDate = r.DrivingLicenseExpiryDate,
        LengthOfAadhaar = r.LengthOfAadhaar, IdNumber = r.IdNumber,
        CertifiedCopyWithOriginal = r.CertifiedCopyWithOriginal, EquivalentEDoc = r.EquivalentEdoc,
        VerifiedFromDigiLocker = r.VerifiedFromDigiLocker, PresenceInMeaRepository = r.PresenceInMeaRepository,
        PresenceInEciRepository = r.PresenceInEciRepository, PresenceInRtoRepository = r.PresenceInRtoRepository,
        PresenceInNregaRepository = r.PresenceInNregaRepository, PresenceInNprRecords = r.PresenceInNprRecords,
        DataFromOfflineVerification = r.DataFromOfflineVerification, ModeOfAuthentication = r.ModeOfAuthentication,
        EkycDataFromUidai = r.EkycDataFromUidai, CopyOfOvd = r.CopyOfOvd,
    };

    private static AddressDetails ReadAddress(IndividualRecord40Entity r, string pfx)
    {
        string? G(string c) => c switch
        {
            "PermLine1" => r.PermLine1, "PermLine2" => r.PermLine2, "PermLine3" => r.PermLine3,
            "PermCountry" => r.PermCountry, "PermState" => r.PermState, "PermDistrict" => r.PermDistrict,
            "PermCity" => r.PermCity, "PermPinCode" => r.PermPinCode, "PermPinOthers" => r.PermPinOthers,
            "PermDigipin" => r.PermDigipin, "PermSupportedDocument" => r.PermSupportedDocument,
            "PermMatchOvd" => r.PermMatchOvd,
            "CurrLine1" => r.CurrLine1, "CurrLine2" => r.CurrLine2, "CurrLine3" => r.CurrLine3,
            "CurrCountry" => r.CurrCountry, "CurrState" => r.CurrState, "CurrDistrict" => r.CurrDistrict,
            "CurrCity" => r.CurrCity, "CurrPinCode" => r.CurrPinCode, "CurrPinOthers" => r.CurrPinOthers,
            "CurrDigipin" => r.CurrDigipin, "CurrSupportedDocument" => r.CurrSupportedDocument,
            "CurrMatchOvd" => r.CurrMatchOvd,
            _ => null,
        };
        var address = new AddressDetails
        {
            Line1 = G($"{pfx}Line1") ?? "", Line2 = G($"{pfx}Line2") ?? "", Line3 = G($"{pfx}Line3") ?? "",
            Country = G($"{pfx}Country") ?? "", State = G($"{pfx}State") ?? "", District = G($"{pfx}District") ?? "",
            City = G($"{pfx}City") ?? "", PinCode = G($"{pfx}PinCode") ?? "", PinCodeOthers = G($"{pfx}PinOthers"),
            Digipin = G($"{pfx}Digipin"), AddressSupportedWithDocument = G($"{pfx}SupportedDocument") ?? "Y",
            AddressMatchWithOvd = G($"{pfx}MatchOvd") ?? "Exact Match",
        };
        if (pfx == "Curr")
        {
            address.ProofOfAddress = r.CurrProofOfAddress;
            address.ProofOfAddressType = r.CurrProofOfAddressType;
            address.LengthOfAadhaar = r.CurrLengthOfAadhaar;
            address.IdNumber = r.CurrIdNumber;
            address.ModeOfAadhaarVerification = r.CurrAadhaarVerification;
            address.OvdExpiryDate = r.CurrOvdExpiryDate;
            address.DeemedPoa = r.CurrDeemedPoa;
            address.DeemedPoaVerified = r.CurrDeemedPoaVerified;
            address.CertifiedCopyWithOriginal = r.CurrCertifiedCopy;
            address.EquivalentEDoc = r.CurrEquivalentEdoc;
            address.VerifiedFromDigiLocker = r.CurrDigiLockerVerified;
            address.RemoteGeoTagging = r.CurrRemoteGeoTagging;
            address.AddressExactlyMatch = r.CurrAddressExactlyMatch;
            address.PositiveVerification = r.CurrPositiveVerification;
            address.PhysicalVerificationByThirdParty = r.CurrPhysicalThirdParty;
            address.PhysicalVerificationByReOfficial = r.CurrPhysicalReOfficial;
            address.PresenceInRepository = r.CurrPresenceInRepository;
            address.ForeignGovernmentDocument = r.CurrForeignGovDocument;
            address.CopyOfOvd = r.CurrCopyOfOvd;
        }
        return address;
    }

    private static ContactDetails ReadRecord50(IndividualRecord50Entity r) => new()
    {
        Email = r.EmailAddress ?? "",
        CountryCode = r.CountryCode ?? "+91",
        MobileNumber = r.MobileNumber ?? "",
        MobileValidatedViaOtp = r.MobileValidatedViaOtp,
        EmailValidatedViaOtp = r.EmailValidatedViaOtp,
        MobileValidatedViaThirdParty = r.MobileValidatedViaThirdParty,
    };

    private static RelatedParty ReadRecord60(IndividualRecord60Entity r) => new()
    {
        RelatedPersonType = r.RelatedPersonType ?? "",
        CkycNumberOfRelatedPerson = r.CkycNumberOfRelatedPerson ?? "",
    };

    private static OtherDetails ReadRecord70(IndividualRecord70Entity r) => new()
    {
        Remarks = r.Remarks ?? "", VideoKycWithoutOfficial = r.VideoKycWithoutOfficial ?? "N",
        VideoKycWithReOfficial = r.VideoKycWithReOfficial ?? "N", FaceToFaceWithReOfficial = r.FaceToFaceWithReOfficial ?? "N",
        NonFaceToFace = r.NonFaceToFace ?? "N", FaceToFaceWithNonOfficial = r.FaceToFaceWithNonOfficial ?? "N",
        AttestationDate = r.AttestationDate ?? "", EmployeeName = r.EmployeeName ?? "",
        EmployeeCode = r.EmployeeCode ?? "", EmployeeDesignation = r.EmployeeDesignation ?? "",
        EmployeeBranch = r.EmployeeBranch ?? "", EmployeeCkycId = r.EmployeeCkycId ?? "",
        InstitutionName = r.InstitutionName ?? "", InstitutionCode = r.InstitutionCode ?? "",
        DeclarationDocument = r.DeclarationDocument ?? "", DeclarationFlag = r.DeclarationFlag ?? "Y",
        ClientConsent = r.ClientConsent ?? "", Place = r.Place ?? "", DeclarationDate = r.DeclarationDate ?? "",
    };

    private static void InsertRecord20(CkycDbContext db, Individual r, DateTime now)
    {
        db.IndividualRecord20s.Add(new IndividualRecord20Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId,
            SearchKey = r.SearchKey, KycType = r.KycType,
            NameTitle = r.Name.Title, NameFirst = r.Name.FirstName, NameMiddle = r.Name.MiddleName, NameLast = r.Name.LastName,
            MaidenTitle = r.MaidenName.Title, MaidenFirst = r.MaidenName.FirstName, MaidenMiddle = r.MaidenName.MiddleName, MaidenLast = r.MaidenName.LastName,
            MotherTitle = r.MotherName.Title, MotherFirst = r.MotherName.FirstName, MotherMiddle = r.MotherName.MiddleName, MotherLast = r.MotherName.LastName,
            FatherTitle = r.FatherName.Title, FatherFirst = r.FatherName.FirstName, FatherMiddle = r.FatherName.MiddleName, FatherLast = r.FatherName.LastName,
            SpouseTitle = r.SpouseName.Title, SpouseFirst = r.SpouseName.FirstName, SpouseMiddle = r.SpouseName.MiddleName, SpouseLast = r.SpouseName.LastName,
            DateOfBirth = r.DateOfBirth, Gender = r.Gender,
            ResidentialStatus = r.ResidentialStatus, ResidentialSupportedByDocument = r.ResidentialStatusSupportedByDocument,
            Nationality = r.Nationality, NationalitySupportedByDocument = r.NationalitySupportedByDocument,
            DifferentlyAbledStatus = r.DifferentlyAbledStatus, DifferentlyAbledType = r.DifferentlyAbledType,
            Pan = r.Pan, PanVerified = r.PanVerified, PhotoOfIndividual = r.PhotoOfIndividual,
            Minor = r.Minor, DoBmatchWithOvd = r.DateOfBirthMatchWithOvd, NameMatchWithOvd = r.NameMatchWithOvd,
            PhotoMatchWithOvd = r.PhotoProvidedMatchWithOvd, GenderProvidedInOvd = r.GenderProvidedInOvd, GenderMatchWithOvd = r.GenderMatchWithOvd,
            Form97Provided = r.Form97Provided, Form61Provided = r.Form61Provided, PanDocument = r.PanDocument,
            OtherTypeOfImpairment = r.OtherTypeOfImpairment, DisabilityReferenceNumber = r.DisabilityReferenceNumber,
            PermanentDisability = r.PermanentDisability, DisabilityDate = r.DisabilityDate, PercentageOfImpairment = r.PercentageOfImpairment,
            DifferentlyAbledSupportedByDocument = r.DifferentlyAbledSupportedByDocument,
            CreatedAt = now, UpdatedAt = now,
        });
    }

    private static void InsertRecord30(CkycDbContext db, long masterId, string customerId, ProofOfIdentity p, DateTime now)
    {
        db.IndividualRecord30s.Add(new IndividualRecord30Entity
        {
            MasterRecordId = masterId, CustomerId = customerId, Record20LineNumber = 1,
            OvdType = p.OvdType, ModeOfAadhaarVerification = p.ModeOfAadhaarVerification,
            PassportExpiryDate = p.PassportExpiryDate, DrivingLicenseExpiryDate = p.DrivingLicenseExpiryDate,
            LengthOfAadhaar = p.LengthOfAadhaar, IdNumber = p.IdNumber,
            CertifiedCopyWithOriginal = p.CertifiedCopyWithOriginal, EquivalentEdoc = p.EquivalentEDoc,
            VerifiedFromDigiLocker = p.VerifiedFromDigiLocker, PresenceInMeaRepository = p.PresenceInMeaRepository,
            PresenceInEciRepository = p.PresenceInEciRepository, PresenceInRtoRepository = p.PresenceInRtoRepository,
            PresenceInNregaRepository = p.PresenceInNregaRepository, PresenceInNprRecords = p.PresenceInNprRecords,
            DataFromOfflineVerification = p.DataFromOfflineVerification, ModeOfAuthentication = p.ModeOfAuthentication,
            EkycDataFromUidai = p.EkycDataFromUidai, CopyOfOvd = p.CopyOfOvd,
        });
    }

    private static void InsertRecord40(CkycDbContext db, Individual r)
    {
        var perm = r.PermanentAddress;
        var curr = r.CurrentAddress;
        db.IndividualRecord40s.Add(new IndividualRecord40Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId, Record20LineNumber = 1,
            PermLine1 = perm?.Line1, PermLine2 = perm?.Line2, PermLine3 = perm?.Line3,
            PermCountry = perm?.Country, PermState = perm?.State, PermDistrict = perm?.District,
            PermCity = perm?.City, PermPinCode = perm?.PinCode, PermPinOthers = perm?.PinCodeOthers,
            PermDigipin = perm?.Digipin, PermSupportedDocument = perm?.AddressSupportedWithDocument, PermMatchOvd = perm?.AddressMatchWithOvd,
            CurrSameAsPermanent = r.CurrentAddressSameAsPermanent,
            CurrLine1 = curr?.Line1, CurrLine2 = curr?.Line2, CurrLine3 = curr?.Line3,
            CurrCountry = curr?.Country, CurrState = curr?.State, CurrDistrict = curr?.District,
            CurrCity = curr?.City, CurrPinCode = curr?.PinCode, CurrPinOthers = curr?.PinCodeOthers,
            CurrDigipin = curr?.Digipin, CurrSupportedDocument = curr?.AddressSupportedWithDocument, CurrMatchOvd = curr?.AddressMatchWithOvd,
            CurrProofOfAddress = curr?.ProofOfAddress, CurrProofOfAddressType = curr?.ProofOfAddressType,
            CurrLengthOfAadhaar = curr?.LengthOfAadhaar, CurrIdNumber = curr?.IdNumber,
            CurrAadhaarVerification = curr?.ModeOfAadhaarVerification, CurrOvdExpiryDate = curr?.OvdExpiryDate,
            CurrDeemedPoa = curr?.DeemedPoa, CurrDeemedPoaVerified = curr?.DeemedPoaVerified,
            CurrCertifiedCopy = curr?.CertifiedCopyWithOriginal, CurrEquivalentEdoc = curr?.EquivalentEDoc,
            CurrDigiLockerVerified = curr?.VerifiedFromDigiLocker, CurrRemoteGeoTagging = curr?.RemoteGeoTagging,
            CurrAddressExactlyMatch = curr?.AddressExactlyMatch, CurrPositiveVerification = curr?.PositiveVerification,
            CurrPhysicalThirdParty = curr?.PhysicalVerificationByThirdParty, CurrPhysicalReOfficial = curr?.PhysicalVerificationByReOfficial,
            CurrPresenceInRepository = curr?.PresenceInRepository, CurrForeignGovDocument = curr?.ForeignGovernmentDocument,
            CurrCopyOfOvd = curr?.CopyOfOvd,
        });
    }

    private static void InsertRecord50(CkycDbContext db, Individual r)
    {
        var c = r.Contact;
        db.IndividualRecord50s.Add(new IndividualRecord50Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId, Record20LineNumber = 1,
            EmailAddress = c?.Email, CountryCode = c?.CountryCode, MobileNumber = c?.MobileNumber,
            MobileValidatedViaOtp = c?.MobileValidatedViaOtp, EmailValidatedViaOtp = c?.EmailValidatedViaOtp,
            MobileValidatedViaThirdParty = c?.MobileValidatedViaThirdParty,
        });
    }

    private static void InsertRecord60(CkycDbContext db, long masterId, string customerId, RelatedParty rp)
    {
        db.IndividualRecord60s.Add(new IndividualRecord60Entity
        {
            MasterRecordId = masterId, CustomerId = customerId, Record20LineNumber = 1,
            RelatedPersonType = rp.RelatedPersonType, CkycNumberOfRelatedPerson = rp.CkycNumberOfRelatedPerson,
        });
    }

    private static void InsertRecord70(CkycDbContext db, Individual r)
    {
        var o = r.Other;
        db.IndividualRecord70s.Add(new IndividualRecord70Entity
        {
            MasterRecordId = r.MasterRecordId, CustomerId = r.CustomerId, Record20LineNumber = 1,
            Remarks = o?.Remarks, VideoKycWithoutOfficial = o?.VideoKycWithoutOfficial,
            VideoKycWithReOfficial = o?.VideoKycWithReOfficial, FaceToFaceWithReOfficial = o?.FaceToFaceWithReOfficial,
            NonFaceToFace = o?.NonFaceToFace, FaceToFaceWithNonOfficial = o?.FaceToFaceWithNonOfficial,
            AttestationDate = o?.AttestationDate, EmployeeName = o?.EmployeeName, EmployeeCode = o?.EmployeeCode,
            EmployeeDesignation = o?.EmployeeDesignation, EmployeeBranch = o?.EmployeeBranch, EmployeeCkycId = o?.EmployeeCkycId,
            InstitutionName = o?.InstitutionName, InstitutionCode = o?.InstitutionCode,
            DeclarationDocument = o?.DeclarationDocument, DeclarationFlag = o?.DeclarationFlag,
            ClientConsent = o?.ClientConsent, Place = o?.Place, DeclarationDate = o?.DeclarationDate,
        });
    }

    private static async Task DeleteExistingAsync(CkycDbContext db, long masterId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM individual_record_20 WHERE MasterRecordId={{masterId}};
            DELETE FROM individual_record_30 WHERE MasterRecordId={{masterId}};
            DELETE FROM individual_record_40 WHERE MasterRecordId={{masterId}};
            DELETE FROM individual_record_50 WHERE MasterRecordId={{masterId}};
            DELETE FROM individual_record_60 WHERE MasterRecordId={{masterId}};
            DELETE FROM individual_record_70 WHERE MasterRecordId={{masterId}};
            """, ct);
    }
}
