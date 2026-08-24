using System.Data;
using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using static CKYC.Data.MasterRepository;

namespace CKYC.Data;

public sealed class IndividualRepository : IIndividualRepository
{
    private readonly ICkycDatabase _db;

    public IndividualRepository(ICkycDatabase db) => _db = db;

    public async Task<SaveRecordResult> SaveAsync(Individual record, CancellationToken ct = default)
    {
        if (record.MasterRecordId <= 0)
            return new SaveRecordResult(record.MasterRecordId, false, "MasterRecordId is required", null);

        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var localTx = (DbTransaction)tx;

        try
        {
            await DeleteExistingAsync(conn, localTx, record.MasterRecordId, ct);

            var now = DateTime.UtcNow.ToString("o");
            await InsertRecord20(conn, localTx, record, now, ct);
            foreach (var proof in record.Proofs) await InsertRecord30(conn, localTx, record.MasterRecordId, proof, ct);
            await InsertRecord40(conn, localTx, record, ct);
            await InsertRecord50(conn, localTx, record, ct);
            foreach (var rp in record.RelatedParties) await InsertRecord60(conn, localTx, record.MasterRecordId, rp, ct);
            await InsertRecord70(conn, localTx, record, ct);

            await tx.CommitAsync(ct);
            return new SaveRecordResult(record.MasterRecordId, true, null,
                $"Saved demographics + {record.Proofs.Count} proof(s), addresses, contact, " +
                $"{record.RelatedParties.Count} related party(ies), attestation");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return new SaveRecordResult(record.MasterRecordId, false, ex.Message, null);
        }
    }

    public async Task<IReadOnlyList<Individual>> GetBySourceCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return Array.Empty<Individual>();
        var placeholders = string.Join(",", customerIds.Select((_, i) => $"@v{i}"));

        await using var conn = _db.Create();
        var result = new List<Individual>();
        var masterIdByCustomer = new Dictionary<string, long>();
        await using (var cmd = conn.CreateCommand())
        {
            var i = 0;
            foreach (var id in customerIds) cmd.Parameters.Add(NewParam($"@v{i++}", id));
            cmd.CommandText = $"SELECT * FROM kyc_record_20 WHERE SourceCustomerId IN ({placeholders})";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var ind = ReadRecord20(r);
                ind.MasterRecordId = Convert.ToInt64(r["MasterRecordId"]);
                ind.SourceCustomerId = r["SourceCustomerId"] as string ?? string.Empty;
                ind.Proofs = new List<ProofOfIdentity>();
                ind.RelatedParties = new List<RelatedParty>();
                result.Add(ind);
                masterIdByCustomer[ind.SourceCustomerId] = ind.MasterRecordId;
            }
        }

        foreach (var ind in result)
        {
            ind.Proofs = await LoadRecord30Async(conn, ind.MasterRecordId, ct);
            ind.PermanentAddress = await LoadPermanentAddressAsync(conn, ind.MasterRecordId, ct);
            ind.CurrentAddress = await LoadCurrentAddressAsync(conn, ind.MasterRecordId, ct);
            ind.Contact = await LoadRecord50Async(conn, ind.MasterRecordId, ct);
            ind.RelatedParties = await LoadRecord60Async(conn, ind.MasterRecordId, ct);
            ind.Other = await LoadRecord70Async(conn, ind.MasterRecordId, ct);
        }
        return result;
    }

    private static async Task<List<ProofOfIdentity>> LoadRecord30Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        var list = new List<ProofOfIdentity>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM kyc_record_30 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            string? G(string c) => r[c] as string;
            list.Add(new ProofOfIdentity
            {
                OvdType = G("OvdType") ?? "", ModeOfAadhaarVerification = G("ModeOfAadhaarVerification") ?? "",
                PassportExpiryDate = G("PassportExpiryDate"), DrivingLicenseExpiryDate = G("DrivingLicenseExpiryDate"),
                LengthOfAadhaar = G("LengthOfAadhaar"), IdNumber = G("IdNumber"),
                CertifiedCopyWithOriginal = G("CertifiedCopyWithOriginal"), EquivalentEDoc = G("EquivalentEDoc"),
                VerifiedFromDigiLocker = G("VerifiedFromDigiLocker"), PresenceInMeaRepository = G("PresenceInMeaRepository"),
                PresenceInEciRepository = G("PresenceInEciRepository"), PresenceInRtoRepository = G("PresenceInRtoRepository"),
                PresenceInNregaRepository = G("PresenceInNregaRepository"), PresenceInNprRecords = G("PresenceInNprRecords"),
                DataFromOfflineVerification = G("DataFromOfflineVerification"), ModeOfAuthentication = G("ModeOfAuthentication"),
                EkycDataFromUidai = G("EkycDataFromUidai"), CopyOfOvd = G("CopyOfOvd"),
            });
        }
        return list;
    }

    private static async Task<AddressDetails?> LoadPermanentAddressAsync(DbConnection conn, long masterId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM kyc_record_40 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadAddress(r, "Perm") : null;
    }

    private static async Task<AddressDetails?> LoadCurrentAddressAsync(DbConnection conn, long masterId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM kyc_record_40 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadAddress(r, "Curr") : null;
    }

    private static AddressDetails ReadAddress(DbDataReader r, string pfx)
    {
        string? G(string c) => r[c] as string;
        return new AddressDetails
        {
            Line1 = G($"{pfx}Line1") ?? "", Line2 = G($"{pfx}Line2") ?? "", Line3 = G($"{pfx}Line3") ?? "",
            Country = G($"{pfx}Country") ?? "", State = G($"{pfx}State") ?? "", District = G($"{pfx}District") ?? "",
            City = G($"{pfx}City") ?? "", PinCode = G($"{pfx}PinCode") ?? "", PinCodeOthers = G($"{pfx}PinOthers"),
            Digipin = G($"{pfx}Digipin"), AddressSupportedWithDocument = G($"{pfx}SupportedDocument") ?? "Y",
            AddressMatchWithOvd = G($"{pfx}MatchOvd") ?? "Y",
        };
    }

    private static async Task<ContactDetails?> LoadRecord50Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM kyc_record_50 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ContactDetails
        {
            Email = r["EmailAddress"] as string ?? "",
            CountryCode = r["CountryCode"] as string ?? "+91",
            MobileNumber = r["MobileNumber"] as string ?? "",
            MobileValidatedViaOtp = r["MobileValidatedViaOtp"] as string,
            EmailValidatedViaOtp = r["EmailValidatedViaOtp"] as string,
            MobileValidatedViaThirdParty = r["MobileValidatedViaThirdParty"] as string,
        };
    }

    private static async Task<List<RelatedParty>> LoadRecord60Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        var list = new List<RelatedParty>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM kyc_record_60 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new RelatedParty { RelatedPersonType = r["RelatedPersonType"] as string ?? "", CkycNumberOfRelatedPerson = r["CkycNumberOfRelatedPerson"] as string ?? "" });
        return list;
    }

    private static async Task<OtherDetails?> LoadRecord70Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM kyc_record_70 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        string? G(string c) => r[c] as string;
        return new OtherDetails
        {
            Remarks = G("Remarks") ?? "", VideoKycWithoutOfficial = G("VideoKycWithoutOfficial") ?? "N",
            VideoKycWithReOfficial = G("VideoKycWithReOfficial") ?? "N", FaceToFaceWithReOfficial = G("FaceToFaceWithReOfficial") ?? "N",
            NonFaceToFace = G("NonFaceToFace") ?? "N", FaceToFaceWithNonOfficial = G("FaceToFaceWithNonOfficial") ?? "N",
            AttestationDate = G("AttestationDate") ?? "", EmployeeName = G("EmployeeName") ?? "",
            EmployeeCode = G("EmployeeCode") ?? "", EmployeeDesignation = G("EmployeeDesignation") ?? "",
            EmployeeBranch = G("EmployeeBranch") ?? "", EmployeeCkycId = G("EmployeeCkycId") ?? "",
            InstitutionName = G("InstitutionName") ?? "", InstitutionCode = G("InstitutionCode") ?? "",
            DeclarationDocument = G("DeclarationDocument") ?? "", DeclarationFlag = G("DeclarationFlag") ?? "Y",
            ClientConsent = G("ClientConsent") ?? "", Place = G("Place") ?? "", DeclarationDate = G("DeclarationDate") ?? "",
        };
    }

    // ---------- record 20 ----------
    private static async Task InsertRecord20(DbConnection conn, DbTransaction tx, Individual r, string now, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO kyc_record_20
                (MasterRecordId, SourceCustomerId, SearchKey, KycType, NameTitle, NameFirst, NameMiddle, NameLast,
                 MaidenTitle, MaidenFirst, MaidenMiddle, MaidenLast, MotherTitle, MotherFirst, MotherMiddle, MotherLast,
                 FatherTitle, FatherFirst, FatherMiddle, FatherLast, SpouseTitle, SpouseFirst, SpouseMiddle, SpouseLast,
                 DateOfBirth, Gender, ResidentialStatus, ResidentialSupportedByDocument, Nationality, NationalitySupportedByDocument,
                 DifferentlyAbledStatus, DifferentlyAbledType, Pan, PanVerified, PhotoOfIndividual,
                 Minor, DoBMatchWithOvd, NameMatchWithOvd, PhotoMatchWithOvd, GenderProvidedInOvd, GenderMatchWithOvd,
                 Form97Provided, Form61Provided, PanDocument, OtherTypeOfImpairment, DisabilityReferenceNumber,
                 PermanentDisability, DisabilityDate, PercentageOfImpairment, DifferentlyAbledSupportedByDocument,
                 CreatedAt, UpdatedAt)
            VALUES
                (@m, @sid, @sk, @kyc, @nT, @nF, @nM, @nL, @mdT, @mdF, @mdM, @mdL, @moT, @moF, @moM, @moL,
                 @faT, @faF, @faM, @faL, @spT, @spF, @spM, @spL, @dob, @gen, @rs, @rsd, @nat, @natd,
                 @daS, @daT, @pan, @panV, @photo,
                 @minor, @dobm, @namem, @photom, @genProv, @genMatch, @f97, @f61, @panDoc, @otherImp, @disRef,
                 @permDis, @disDate, @pctImp, @daSup, @now, @now)
            """;
        Add(cmd, "@m", r.MasterRecordId); Add(cmd, "@sid", r.SourceCustomerId);
        Add(cmd, "@sk", r.SearchKey); Add(cmd, "@kyc", r.KycType);
        Add(cmd, "@nT", r.Name.Title); Add(cmd, "@nF", r.Name.FirstName); Add(cmd, "@nM", r.Name.MiddleName); Add(cmd, "@nL", r.Name.LastName);
        Add(cmd, "@mdT", r.MaidenName.Title); Add(cmd, "@mdF", r.MaidenName.FirstName); Add(cmd, "@mdM", r.MaidenName.MiddleName); Add(cmd, "@mdL", r.MaidenName.LastName);
        Add(cmd, "@moT", r.MotherName.Title); Add(cmd, "@moF", r.MotherName.FirstName); Add(cmd, "@moM", r.MotherName.MiddleName); Add(cmd, "@moL", r.MotherName.LastName);
        Add(cmd, "@faT", r.FatherName.Title); Add(cmd, "@faF", r.FatherName.FirstName); Add(cmd, "@faM", r.FatherName.MiddleName); Add(cmd, "@faL", r.FatherName.LastName);
        Add(cmd, "@spT", r.SpouseName.Title); Add(cmd, "@spF", r.SpouseName.FirstName); Add(cmd, "@spM", r.SpouseName.MiddleName); Add(cmd, "@spL", r.SpouseName.LastName);
        Add(cmd, "@dob", r.DateOfBirth); Add(cmd, "@gen", r.Gender);
        Add(cmd, "@rs", r.ResidentialStatus); Add(cmd, "@rsd", r.ResidentialStatusSupportedByDocument);
        Add(cmd, "@nat", r.Nationality); Add(cmd, "@natd", r.NationalitySupportedByDocument);
        Add(cmd, "@daS", r.DifferentlyAbledStatus); Add(cmd, "@daT", r.DifferentlyAbledType);
        Add(cmd, "@pan", r.Pan); Add(cmd, "@panV", r.PanVerified); Add(cmd, "@photo", r.PhotoOfIndividual);
        Add(cmd, "@minor", r.Minor); Add(cmd, "@dobm", r.DateOfBirthMatchWithOvd); Add(cmd, "@namem", r.NameMatchWithOvd);
        Add(cmd, "@photom", r.PhotoProvidedMatchWithOvd); Add(cmd, "@genProv", r.GenderProvidedInOvd); Add(cmd, "@genMatch", r.GenderMatchWithOvd);
        Add(cmd, "@f97", r.Form97Provided); Add(cmd, "@f61", r.Form61Provided); Add(cmd, "@panDoc", r.PanDocument);
        Add(cmd, "@otherImp", r.OtherTypeOfImpairment); Add(cmd, "@disRef", r.DisabilityReferenceNumber);
        Add(cmd, "@permDis", r.PermanentDisability); Add(cmd, "@disDate", r.DisabilityDate); Add(cmd, "@pctImp", r.PercentageOfImpairment);
        Add(cmd, "@daSup", r.DifferentlyAbledSupportedByDocument);
        Add(cmd, "@now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord30(DbConnection conn, DbTransaction tx, long masterId, ProofOfIdentity p, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO kyc_record_30
                (MasterRecordId, Record20LineNumber, OvdType, ModeOfAadhaarVerification, PassportExpiryDate,
                 DrivingLicenseExpiryDate, LengthOfAadhaar, IdNumber, CertifiedCopyWithOriginal, EquivalentEDoc,
                 VerifiedFromDigiLocker, PresenceInMeaRepository, PresenceInEciRepository, PresenceInRtoRepository,
                 PresenceInNregaRepository, PresenceInNprRecords, DataFromOfflineVerification, ModeOfAuthentication,
                 EkycDataFromUidai, CopyOfOvd)
            VALUES
                (@m, 1, @ovd, @mode, @pe, @dle, @len, @idn, @cert, @eq, @digi, @mea, @eci, @rto, @nrega, @npr, @off, @auth, @ekyc, @copy)
            """;
        Add(cmd, "@m", masterId); Add(cmd, "@ovd", p.OvdType); Add(cmd, "@mode", p.ModeOfAadhaarVerification);
        Add(cmd, "@pe", p.PassportExpiryDate); Add(cmd, "@dle", p.DrivingLicenseExpiryDate); Add(cmd, "@len", p.LengthOfAadhaar);
        Add(cmd, "@idn", p.IdNumber); Add(cmd, "@cert", p.CertifiedCopyWithOriginal); Add(cmd, "@eq", p.EquivalentEDoc);
        Add(cmd, "@digi", p.VerifiedFromDigiLocker); Add(cmd, "@mea", p.PresenceInMeaRepository); Add(cmd, "@eci", p.PresenceInEciRepository);
        Add(cmd, "@rto", p.PresenceInRtoRepository); Add(cmd, "@nrega", p.PresenceInNregaRepository); Add(cmd, "@npr", p.PresenceInNprRecords);
        Add(cmd, "@off", p.DataFromOfflineVerification); Add(cmd, "@auth", p.ModeOfAuthentication); Add(cmd, "@ekyc", p.EkycDataFromUidai); Add(cmd, "@copy", p.CopyOfOvd);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord40(DbConnection conn, DbTransaction tx, Individual r, CancellationToken ct)
    {
        var perm = r.PermanentAddress;
        var curr = r.CurrentAddress;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO kyc_record_40
                (MasterRecordId, Record20LineNumber, PermLine1, PermLine2, PermLine3, PermCountry, PermState, PermDistrict, PermCity, PermPinCode, PermPinOthers, PermDigipin, PermSupportedDocument, PermMatchOvd,
                 CurrLine1, CurrLine2, CurrLine3, CurrCountry, CurrState, CurrDistrict, CurrCity, CurrPinCode, CurrPinOthers, CurrDigipin, CurrSupportedDocument, CurrMatchOvd)
            VALUES
                (@m, 1, @p1,@p2,@p3,@pco,@pst,@pdi,@pci,@ppin,@ppinO,@pdig,@psup,@pmatch,
                 @c1,@c2,@c3,@cco,@cst,@cdi,@cci,@cpin,@cpinO,@cdig,@csup,@cmatch)
            """;
        Add(cmd, "@m", r.MasterRecordId);
        Add(cmd, "@p1", perm?.Line1); Add(cmd, "@p2", perm?.Line2); Add(cmd, "@p3", perm?.Line3);
        Add(cmd, "@pco", perm?.Country); Add(cmd, "@pst", perm?.State); Add(cmd, "@pdi", perm?.District);
        Add(cmd, "@pci", perm?.City); Add(cmd, "@ppin", perm?.PinCode); Add(cmd, "@ppinO", perm?.PinCodeOthers);
        Add(cmd, "@pdig", perm?.Digipin); Add(cmd, "@psup", perm?.AddressSupportedWithDocument); Add(cmd, "@pmatch", perm?.AddressMatchWithOvd);
        Add(cmd, "@c1", curr?.Line1); Add(cmd, "@c2", curr?.Line2); Add(cmd, "@c3", curr?.Line3);
        Add(cmd, "@cco", curr?.Country); Add(cmd, "@cst", curr?.State); Add(cmd, "@cdi", curr?.District);
        Add(cmd, "@cci", curr?.City); Add(cmd, "@cpin", curr?.PinCode); Add(cmd, "@cpinO", curr?.PinCodeOthers);
        Add(cmd, "@cdig", curr?.Digipin); Add(cmd, "@csup", curr?.AddressSupportedWithDocument); Add(cmd, "@cmatch", curr?.AddressMatchWithOvd);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord50(DbConnection conn, DbTransaction tx, Individual r, CancellationToken ct)
    {
        var c = r.Contact;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO kyc_record_50 (MasterRecordId, Record20LineNumber, EmailAddress, CountryCode, MobileNumber, MobileValidatedViaOtp, EmailValidatedViaOtp, MobileValidatedViaThirdParty)
            VALUES (@m, 1, @email, @cc, @mob, @mOtp, @eOtp, @mTp)
            """;
        Add(cmd, "@m", r.MasterRecordId); Add(cmd, "@email", c?.Email); Add(cmd, "@cc", c?.CountryCode);
        Add(cmd, "@mob", c?.MobileNumber); Add(cmd, "@mOtp", c?.MobileValidatedViaOtp); Add(cmd, "@eOtp", c?.EmailValidatedViaOtp); Add(cmd, "@mTp", c?.MobileValidatedViaThirdParty);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord60(DbConnection conn, DbTransaction tx, long masterId, RelatedParty rp, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO kyc_record_60 (MasterRecordId, Record20LineNumber, RelatedPersonType, CkycNumberOfRelatedPerson)
            VALUES (@m, 1, @type, @ckyc)
            """;
        Add(cmd, "@m", masterId); Add(cmd, "@type", rp.RelatedPersonType); Add(cmd, "@ckyc", rp.CkycNumberOfRelatedPerson);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord70(DbConnection conn, DbTransaction tx, Individual r, CancellationToken ct)
    {
        var o = r.Other;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO kyc_record_70 (MasterRecordId, Record20LineNumber, Remarks, VideoKycWithoutOfficial, VideoKycWithReOfficial,
                FaceToFaceWithReOfficial, NonFaceToFace, FaceToFaceWithNonOfficial, AttestationDate, EmployeeName, EmployeeCode,
                EmployeeDesignation, EmployeeBranch, EmployeeCkycId, InstitutionName, InstitutionCode, DeclarationDocument,
                DeclarationFlag, ClientConsent, Place, DeclarationDate)
            VALUES (@m, 1, @rem, @v1, @v2, @f1, @nf, @f2, @ad, @en, @ec, @ed, @eb, @eid, @in, @ic, @dd, @df, @cc, @pl, @dc)
            """;
        Add(cmd, "@m", r.MasterRecordId); Add(cmd, "@rem", o?.Remarks); Add(cmd, "@v1", o?.VideoKycWithoutOfficial);
        Add(cmd, "@v2", o?.VideoKycWithReOfficial); Add(cmd, "@f1", o?.FaceToFaceWithReOfficial); Add(cmd, "@nf", o?.NonFaceToFace);
        Add(cmd, "@f2", o?.FaceToFaceWithNonOfficial); Add(cmd, "@ad", o?.AttestationDate); Add(cmd, "@en", o?.EmployeeName);
        Add(cmd, "@ec", o?.EmployeeCode); Add(cmd, "@ed", o?.EmployeeDesignation); Add(cmd, "@eb", o?.EmployeeBranch);
        Add(cmd, "@eid", o?.EmployeeCkycId); Add(cmd, "@in", o?.InstitutionName); Add(cmd, "@ic", o?.InstitutionCode);
        Add(cmd, "@dd", o?.DeclarationDocument); Add(cmd, "@df", o?.DeclarationFlag); Add(cmd, "@cc", o?.ClientConsent);
        Add(cmd, "@pl", o?.Place); Add(cmd, "@dc", o?.DeclarationDate);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteExistingAsync(DbConnection conn, DbTransaction tx, long masterId, CancellationToken ct)
    {
        foreach (var table in new[] { "kyc_record_20", "kyc_record_30", "kyc_record_40", "kyc_record_50", "kyc_record_60", "kyc_record_70" })
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE MasterRecordId=@m";
            cmd.Parameters.Add(NewParam("@m", masterId));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static Individual ReadRecord20(DbDataReader r)
    {
        string? G(string c) => r[c] as string;
        return new Individual
        {
            Id = Convert.ToInt64(r["Id"]),
            SearchKey = G("SearchKey") ?? "",
            KycType = G("KycType") ?? "",
            Name = new PersonName { Title = G("NameTitle") ?? "", FirstName = G("NameFirst") ?? "", MiddleName = G("NameMiddle") ?? "", LastName = G("NameLast") ?? "" },
            MaidenName = new PersonName { Title = G("MaidenTitle") ?? "", FirstName = G("MaidenFirst") ?? "", MiddleName = G("MaidenMiddle") ?? "", LastName = G("MaidenLast") ?? "" },
            MotherName = new PersonName { Title = G("MotherTitle") ?? "", FirstName = G("MotherFirst") ?? "", MiddleName = G("MotherMiddle") ?? "", LastName = G("MotherLast") ?? "" },
            FatherName = new PersonName { Title = G("FatherTitle") ?? "", FirstName = G("FatherFirst") ?? "", MiddleName = G("FatherMiddle") ?? "", LastName = G("FatherLast") ?? "" },
            SpouseName = new PersonName { Title = G("SpouseTitle") ?? "", FirstName = G("SpouseFirst") ?? "", MiddleName = G("SpouseMiddle") ?? "", LastName = G("SpouseLast") ?? "" },
            DateOfBirth = G("DateOfBirth"),
            Gender = G("Gender"),
            ResidentialStatus = G("ResidentialStatus"),
            ResidentialStatusSupportedByDocument = G("ResidentialSupportedByDocument"),
            Nationality = G("Nationality"),
            NationalitySupportedByDocument = G("NationalitySupportedByDocument"),
            DifferentlyAbledStatus = G("DifferentlyAbledStatus"),
            DifferentlyAbledType = G("DifferentlyAbledType"),
            Pan = G("Pan"),
            PanVerified = G("PanVerified"),
            PhotoOfIndividual = G("PhotoOfIndividual"),
            Minor = G("Minor"),
            DateOfBirthMatchWithOvd = G("DoBMatchWithOvd"),
            NameMatchWithOvd = G("NameMatchWithOvd"),
            PhotoProvidedMatchWithOvd = G("PhotoMatchWithOvd"),
            GenderProvidedInOvd = G("GenderProvidedInOvd"),
            GenderMatchWithOvd = G("GenderMatchWithOvd"),
            Form97Provided = G("Form97Provided"),
            Form61Provided = G("Form61Provided"),
            PanDocument = G("PanDocument"),
            OtherTypeOfImpairment = G("OtherTypeOfImpairment"),
            DisabilityReferenceNumber = G("DisabilityReferenceNumber"),
            PermanentDisability = G("PermanentDisability"),
            DisabilityDate = G("DisabilityDate"),
            PercentageOfImpairment = G("PercentageOfImpairment"),
            DifferentlyAbledSupportedByDocument = G("DifferentlyAbledSupportedByDocument"),
        };
    }

    private static void Add(DbCommand cmd, string name, object? value)
        => cmd.Parameters.Add(NewParam(name, value));
}
