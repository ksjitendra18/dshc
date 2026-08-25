using System.Data;
using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using CKYC.Core.Models;
using static CKYC.Data.MasterRepository;

namespace CKYC.Data;

public sealed class LegalEntityRepository : ILegalEntityRepository
{
    private readonly ICkycDatabase _db;

    public LegalEntityRepository(ICkycDatabase db) => _db = db;

    public async Task<SaveRecordResult> SaveAsync(LegalEntity record, CancellationToken ct = default)
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
            await InsertRecord30(conn, localTx, record.MasterRecordId, FirstProof(record), ct);
            await InsertRecord40(conn, localTx, record, ct);
            await InsertRecord50(conn, localTx, record, ct);
            foreach (var rp in record.RelatedParties)
                await InsertRecord60(conn, localTx, record.MasterRecordId, rp, ct);
            await InsertRecord70(conn, localTx, record, ct);

            await tx.CommitAsync(ct);
            return new SaveRecordResult(record.MasterRecordId, true, null,
                $"Saved entity details + POI, addresses, contact, {record.RelatedParties.Count} related party(ies), attestation");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return new SaveRecordResult(record.MasterRecordId, false, ex.Message, null);
        }
    }

    public async Task<IReadOnlyList<LegalEntity>> GetBySourceCustomerIdsAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return Array.Empty<LegalEntity>();
        var placeholders = string.Join(",", customerIds.Select((_, i) => $"@v{i}"));

        await using var conn = _db.Create();
        var result = new List<LegalEntity>();
        await using (var cmd = conn.CreateCommand())
        {
            var i = 0;
            foreach (var id in customerIds) cmd.Parameters.Add(NewParam($"@v{i++}", id));
            cmd.CommandText = $"SELECT * FROM legal_entity_record_20 WHERE SourceCustomerId IN ({placeholders})";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var le = ReadRecord20(r);
                le.MasterRecordId = Convert.ToInt64(r["MasterRecordId"]);
                le.SourceCustomerId = r["SourceCustomerId"] as string ?? string.Empty;
                le.Proofs = new List<LeProofOfIdentity>();
                le.RelatedParties = new List<LeRelatedParty>();
                result.Add(le);
            }
        }

        foreach (var le in result)
        {
            le.Proofs = await LoadRecord30Async(conn, le.MasterRecordId, ct);
            await LoadRecord40Async(conn, le, ct);
            le.Contact = await LoadRecord50Async(conn, le.MasterRecordId, ct);
            le.RelatedParties = await LoadRecord60Async(conn, le.MasterRecordId, ct);
            le.Other = await LoadRecord70Async(conn, le.MasterRecordId, ct);
        }
        return result;
    }

    // ---------- load helpers ----------
    private static async Task<List<LeProofOfIdentity>> LoadRecord30Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        var list = new List<LeProofOfIdentity>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM legal_entity_record_30 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            string? G(string c) => r[c] as string;
            list.Add(new LeProofOfIdentity
            {
                CertificateOfIncorporation = G("CertificateOfIncorporation"), Cin = G("Cin"),
                MemorandumAndArticles = G("MemorandumAndArticles"), ResolutionBoardPoA = G("ResolutionBoardPoA"),
                NamesSeniorManagement = G("NamesSeniorManagement"), CertificateOfCommencement = G("CertificateOfCommencement"),
                OthersCompany = G("OthersCompany"),
                RegistrationCertificate = G("RegistrationCertificate"), RegistrationNumber = G("RegistrationNumber"),
                LlpinCertificate = G("LlpinCertificate"), Llpin = G("Llpin"), PartnershipDeed = G("PartnershipDeed"),
                NamesAllPartners = G("NamesAllPartners"), OthersPartnership = G("OthersPartnership"),
                TrustRegistrationCertificate = G("TrustRegistrationCertificate"), TrustRegistrationNumber = G("TrustRegistrationNumber"),
                TrustDeed = G("TrustDeed"), NamesBeneficiariesTrustees = G("NamesBeneficiariesTrustees"),
                TrustPowerOfAttorney = G("TrustPowerOfAttorney"), OthersTrust = G("OthersTrust"),
                UnincorporatedRegistrationCertificate = G("UnincorporatedRegCertificate"), UnincorporatedRegistrationNumber = G("UnincorporatedRegNumber"),
                ResolutionManagingBody = G("ResolutionManagingBody"), UnincorporatedPowerOfAttorney = G("UnincorporatedPowerOfAttorney"),
                InfoEstablishExistence = G("InfoEstablishExistence"), OthersUnincorporated = G("OthersUnincorporated"),
                SupportingDocumentsPoi = G("SupportingDocumentsPoi"), OtherTypeRegistrationNumber = G("OtherTypeRegistrationNumber"),
                OtherTypeRegistrationCertificate = G("OtherTypeRegistrationCertificate"), OtherTypePowerOfAttorney = G("OtherTypePowerOfAttorney"),
                ActivityProof1 = G("ActivityProof1"), ActivityProof2 = G("ActivityProof2"), OthersOtherType = G("OthersOtherType"),
            });
        }
        return list;
    }

    private static async Task LoadRecord40Async(DbConnection conn, LegalEntity le, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM legal_entity_record_40 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", le.MasterRecordId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return;
        string? G(string c) => r[c] as string;
        le.RegisteredAddress = ToAddress(G, "Reg");
        le.PrincipalAddress = ToAddress(G, "Prin");
        le.RegisteredAddressDocument = G("RegDocument");
        le.PrincipalAddressDocument = G("PrinDocument");
    }

    private static LeAddressDetails? ToAddress(Func<string, string?> g, string pfx)
        => new()
        {
            Line1 = g($"{pfx}Line1") ?? "", Line2 = g($"{pfx}Line2") ?? "", Line3 = g($"{pfx}Line3") ?? "",
            City = g($"{pfx}City") ?? "", State = g($"{pfx}State") ?? "", District = g($"{pfx}District") ?? "",
            PinCode = g($"{pfx}PinCode") ?? "", PinCodeOthers = g($"{pfx}PinOthers"), Digipin = g($"{pfx}Digipin"),
            Country = g($"{pfx}Country") ?? "IN", ProofOfAddress = g($"{pfx}ProofOfAddress") ?? "A",
            OtherDocumentName = g($"{pfx}OtherDocumentName"), SameAsRegistered = pfx == "Prin" ? g("SameAsRegistered") : null,
        };

    private static async Task<LeContactDetails?> LoadRecord50Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM legal_entity_record_50 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        string? G(string c) => r[c] as string;
        return new LeContactDetails
        {
            CountryCode1 = G("CountryCode1") ?? "+91", MobileNumber1 = G("MobileNumber1"),
            CountryCode2 = G("CountryCode2") ?? "+91", MobileNumber2 = G("MobileNumber2"),
            Email1 = G("EmailId1"), Email2 = G("EmailId2"), Telephone = G("Telephone"), Fax = G("Fax"),
        };
    }

    private static async Task<List<LeRelatedParty>> LoadRecord60Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        var list = new List<LeRelatedParty>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM legal_entity_record_60 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new LeRelatedParty
            {
                Relation = r["Relation"] as string ?? "", CkycNumber = r["CkycNumber"] as string ?? "",
                ControllingInterest = r["ControllingInterest"] as string ?? "",
                PercentageOwnership = r["PercentageOwnership"] as string, OtherRelationName = r["OtherRelationName"] as string,
                Din = r["Din"] as string,
            });
        return list;
    }

    private static async Task<LeOtherDetails?> LoadRecord70Async(DbConnection conn, long masterId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM legal_entity_record_70 WHERE MasterRecordId=@m";
        cmd.Parameters.Add(NewParam("@m", masterId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        string? G(string c) => r[c] as string;
        return new LeOtherDetails
        {
            Remarks = G("Remarks"), CertifiedCopies = G("CertifiedCopies") ?? "Y", EquivalentEDoc = G("EquivalentEDoc") ?? "N",
            VerificationFromDigiLocker = G("VerificationFromDigiLocker") ?? "N", AttestationDate = G("AttestationDate") ?? "",
            EmployeeName = G("EmployeeName") ?? "", EmployeeCode = G("EmployeeCode") ?? "",
            EmployeeDesignation = G("EmployeeDesignation") ?? "", EmployeeBranch = G("EmployeeBranch") ?? "",
            EmployeeCkycId = G("EmployeeCkycId") ?? "", InstitutionName = G("InstitutionName") ?? "",
            InstitutionCode = G("InstitutionCode") ?? "", DeclarationDocument = G("DeclarationDocument") ?? "",
            DeclarationFlag = G("DeclarationFlag") ?? "Y", ConsentDocument = G("ConsentDocument") ?? "",
            Place = G("Place") ?? "", DeclarationDate = G("DeclarationDate") ?? "",
        };
    }

    // ---------- record 20 ----------
    private static async Task InsertRecord20(DbConnection conn, DbTransaction tx, LegalEntity r, string now, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO legal_entity_record_20
                (MasterRecordId, SourceCustomerId, SearchKey, EntityName, EntityConstitution,
                 ListedCompany, RegisteredFirm, RegisteredTrust, DateOfIncorporation, DateOfCommencement,
                 PlaceOfIncorporation, CountryOfIncorporation, TinIssuingCountry, Pan, Form97,
                 TinGstNumber, PanDocument, PanVerified, TinGstnDocument, CreatedAt, UpdatedAt)
            VALUES
                (@m, @sid, @sk, @nm, @con,
                 @listed, @firm, @trust, @doi, @doc,
                 @poi, @coi, @tic, @pan, @f97,
                 @tin, @panDoc, @panV, @tinDoc, @now, @now)
            """;
        Add(cmd, "@m", r.MasterRecordId); Add(cmd, "@sid", r.SourceCustomerId);
        Add(cmd, "@sk", r.SearchKey); Add(cmd, "@nm", r.EntityName); Add(cmd, "@con", r.EntityConstitution);
        Add(cmd, "@listed", r.ListedCompany); Add(cmd, "@firm", r.RegisteredFirm); Add(cmd, "@trust", r.RegisteredTrust);
        Add(cmd, "@doi", r.DateOfIncorporation); Add(cmd, "@doc", r.DateOfCommencement);
        Add(cmd, "@poi", r.PlaceOfIncorporation); Add(cmd, "@coi", r.CountryOfIncorporation);
        Add(cmd, "@tic", r.TinIssuingCountry); Add(cmd, "@pan", r.Pan); Add(cmd, "@f97", r.Form97);
        Add(cmd, "@tin", r.TinGstNumber); Add(cmd, "@panDoc", r.PanDocument); Add(cmd, "@panV", r.PanVerified);
        Add(cmd, "@tinDoc", r.TinGstnDocument);
        Add(cmd, "@now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord30(DbConnection conn, DbTransaction tx, long masterId, LeProofOfIdentity? p, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO legal_entity_record_30
                (MasterRecordId, Record20LineNumber, CertificateOfIncorporation, Cin, MemorandumAndArticles,
                 ResolutionBoardPoA, NamesSeniorManagement, CertificateOfCommencement, OthersCompany,
                 RegistrationCertificate, RegistrationNumber, LlpinCertificate, Llpin, PartnershipDeed,
                 NamesAllPartners, OthersPartnership, TrustRegistrationCertificate, TrustRegistrationNumber,
                 TrustDeed, NamesBeneficiariesTrustees, TrustPowerOfAttorney, OthersTrust,
                 UnincorporatedRegCertificate, UnincorporatedRegNumber, ResolutionManagingBody,
                 UnincorporatedPowerOfAttorney, InfoEstablishExistence, OthersUnincorporated,
                 SupportingDocumentsPoi, OtherTypeRegistrationNumber, OtherTypeRegistrationCertificate,
                 OtherTypePowerOfAttorney, ActivityProof1, ActivityProof2, OthersOtherType)
            VALUES
                (@m, 1, @v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,
                 @v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33)
            """;
        Add(cmd, "@m", masterId);
        Add(cmd, "@v1", p?.CertificateOfIncorporation); Add(cmd, "@v2", p?.Cin); Add(cmd, "@v3", p?.MemorandumAndArticles);
        Add(cmd, "@v4", p?.ResolutionBoardPoA); Add(cmd, "@v5", p?.NamesSeniorManagement); Add(cmd, "@v6", p?.CertificateOfCommencement);
        Add(cmd, "@v7", p?.OthersCompany); Add(cmd, "@v8", p?.RegistrationCertificate); Add(cmd, "@v9", p?.RegistrationNumber);
        Add(cmd, "@v10", p?.LlpinCertificate); Add(cmd, "@v11", p?.Llpin); Add(cmd, "@v12", p?.PartnershipDeed);
        Add(cmd, "@v13", p?.NamesAllPartners); Add(cmd, "@v14", p?.OthersPartnership); Add(cmd, "@v15", p?.TrustRegistrationCertificate);
        Add(cmd, "@v16", p?.TrustRegistrationNumber); Add(cmd, "@v17", p?.TrustDeed); Add(cmd, "@v18", p?.NamesBeneficiariesTrustees);
        Add(cmd, "@v19", p?.TrustPowerOfAttorney); Add(cmd, "@v20", p?.OthersTrust); Add(cmd, "@v21", p?.UnincorporatedRegistrationCertificate);
        Add(cmd, "@v22", p?.UnincorporatedRegistrationNumber); Add(cmd, "@v23", p?.ResolutionManagingBody); Add(cmd, "@v24", p?.UnincorporatedPowerOfAttorney);
        Add(cmd, "@v25", p?.InfoEstablishExistence); Add(cmd, "@v26", p?.OthersUnincorporated); Add(cmd, "@v27", p?.SupportingDocumentsPoi);
        Add(cmd, "@v28", p?.OtherTypeRegistrationNumber); Add(cmd, "@v29", p?.OtherTypeRegistrationCertificate); Add(cmd, "@v30", p?.OtherTypePowerOfAttorney);
        Add(cmd, "@v31", p?.ActivityProof1); Add(cmd, "@v32", p?.ActivityProof2); Add(cmd, "@v33", p?.OthersOtherType);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord40(DbConnection conn, DbTransaction tx, LegalEntity r, CancellationToken ct)
    {
        var reg = r.RegisteredAddress;
        var prin = r.PrincipalAddress;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO legal_entity_record_40
                (MasterRecordId, Record20LineNumber, RegLine1, RegLine2, RegLine3, RegCity, RegState, RegDistrict,
                 RegPinCode, RegPinOthers, RegDigipin, RegCountry, RegProofOfAddress, RegOtherDocumentName, RegDocument,
                 SameAsRegistered,
                 PrinLine1, PrinLine2, PrinLine3, PrinCity, PrinState, PrinDistrict, PrinPinCode, PrinPinOthers,
                 PrinDigipin, PrinCountry, PrinProofOfAddress, PrinOtherDocumentName, PrinDocument)
            VALUES
                (@m, 1, @r1,@r2,@r3,@rci,@rst,@rdi,@rpin,@rpinO,@rdig,@rco,@rpoa,@rod,@rdoc,
                 @same,
                 @p1,@p2,@p3,@pci,@pst,@pdi,@ppin,@ppinO,@pdig,@pco,@ppoa,@pod,@pdoc)
            """;
        Add(cmd, "@m", r.MasterRecordId);
        Add(cmd, "@r1", reg?.Line1); Add(cmd, "@r2", reg?.Line2); Add(cmd, "@r3", reg?.Line3);
        Add(cmd, "@rci", reg?.City); Add(cmd, "@rst", reg?.State); Add(cmd, "@rdi", reg?.District);
        Add(cmd, "@rpin", reg?.PinCode); Add(cmd, "@rpinO", reg?.PinCodeOthers); Add(cmd, "@rdig", reg?.Digipin);
        Add(cmd, "@rco", reg?.Country); Add(cmd, "@rpoa", reg?.ProofOfAddress); Add(cmd, "@rod", reg?.OtherDocumentName);
        Add(cmd, "@rdoc", r.RegisteredAddressDocument);
        Add(cmd, "@same", prin?.SameAsRegistered ?? (prin is null ? "Y" : "N"));
        Add(cmd, "@p1", prin?.Line1); Add(cmd, "@p2", prin?.Line2); Add(cmd, "@p3", prin?.Line3);
        Add(cmd, "@pci", prin?.City); Add(cmd, "@pst", prin?.State); Add(cmd, "@pdi", prin?.District);
        Add(cmd, "@ppin", prin?.PinCode); Add(cmd, "@ppinO", prin?.PinCodeOthers); Add(cmd, "@pdig", prin?.Digipin);
        Add(cmd, "@pco", prin?.Country); Add(cmd, "@ppoa", prin?.ProofOfAddress); Add(cmd, "@pod", prin?.OtherDocumentName);
        Add(cmd, "@pdoc", r.PrincipalAddressDocument);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord50(DbConnection conn, DbTransaction tx, LegalEntity r, CancellationToken ct)
    {
        var c = r.Contact;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO legal_entity_record_50 (MasterRecordId, Record20LineNumber, CountryCode1, MobileNumber1,
                CountryCode2, MobileNumber2, EmailId1, EmailId2, Telephone, Fax)
            VALUES (@m, 1, @cc1, @m1, @cc2, @m2, @e1, @e2, @tel, @fax)
            """;
        Add(cmd, "@m", r.MasterRecordId); Add(cmd, "@cc1", c?.CountryCode1); Add(cmd, "@m1", c?.MobileNumber1);
        Add(cmd, "@cc2", c?.CountryCode2); Add(cmd, "@m2", c?.MobileNumber2); Add(cmd, "@e1", c?.Email1);
        Add(cmd, "@e2", c?.Email2); Add(cmd, "@tel", c?.Telephone); Add(cmd, "@fax", c?.Fax);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord60(DbConnection conn, DbTransaction tx, long masterId, LeRelatedParty rp, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO legal_entity_record_60 (MasterRecordId, Record20LineNumber, Relation, CkycNumber,
                ControllingInterest, PercentageOwnership, OtherRelationName, Din)
            VALUES (@m, 1, @rel, @ckyc, @ci, @pct, @orn, @din)
            """;
        Add(cmd, "@m", masterId); Add(cmd, "@rel", rp.Relation); Add(cmd, "@ckyc", rp.CkycNumber);
        Add(cmd, "@ci", rp.ControllingInterest); Add(cmd, "@pct", rp.PercentageOwnership);
        Add(cmd, "@orn", rp.OtherRelationName); Add(cmd, "@din", rp.Din);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRecord70(DbConnection conn, DbTransaction tx, LegalEntity r, CancellationToken ct)
    {
        var o = r.Other;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO legal_entity_record_70 (MasterRecordId, Record20LineNumber, Remarks, CertifiedCopies,
                EquivalentEDoc, VerificationFromDigiLocker, AttestationDate, EmployeeName, EmployeeCode,
                EmployeeDesignation, EmployeeBranch, EmployeeCkycId, InstitutionName, InstitutionCode,
                DeclarationDocument, DeclarationFlag, ConsentDocument, Place, DeclarationDate)
            VALUES (@m, 1, @rem, @cc, @eq, @digi, @ad, @en, @ec, @ed, @eb, @eid, @in, @ic, @dd, @df, @cons, @pl, @dc)
            """;
        Add(cmd, "@m", r.MasterRecordId); Add(cmd, "@rem", o?.Remarks); Add(cmd, "@cc", o?.CertifiedCopies);
        Add(cmd, "@eq", o?.EquivalentEDoc); Add(cmd, "@digi", o?.VerificationFromDigiLocker); Add(cmd, "@ad", o?.AttestationDate);
        Add(cmd, "@en", o?.EmployeeName); Add(cmd, "@ec", o?.EmployeeCode); Add(cmd, "@ed", o?.EmployeeDesignation);
        Add(cmd, "@eb", o?.EmployeeBranch); Add(cmd, "@eid", o?.EmployeeCkycId); Add(cmd, "@in", o?.InstitutionName);
        Add(cmd, "@ic", o?.InstitutionCode); Add(cmd, "@dd", o?.DeclarationDocument); Add(cmd, "@df", o?.DeclarationFlag);
        Add(cmd, "@cons", o?.ConsentDocument); Add(cmd, "@pl", o?.Place); Add(cmd, "@dc", o?.DeclarationDate);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteExistingAsync(DbConnection conn, DbTransaction tx, long masterId, CancellationToken ct)
    {
        foreach (var table in new[] { "legal_entity_record_20", "legal_entity_record_30", "legal_entity_record_40", "legal_entity_record_50", "legal_entity_record_60", "legal_entity_record_70" })
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE MasterRecordId=@m";
            cmd.Parameters.Add(NewParam("@m", masterId));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static LeProofOfIdentity FirstProof(LegalEntity le)
        => le.Proofs.FirstOrDefault() ?? new LeProofOfIdentity();

    private static LegalEntity ReadRecord20(DbDataReader r)
    {
        string? G(string c) => r[c] as string;
        return new LegalEntity
        {
            Id = Convert.ToInt64(r["Id"]),
            SearchKey = G("SearchKey") ?? "",
            EntityName = G("EntityName") ?? "",
            EntityConstitution = G("EntityConstitution") ?? "",
            ListedCompany = G("ListedCompany"), RegisteredFirm = G("RegisteredFirm"), RegisteredTrust = G("RegisteredTrust"),
            DateOfIncorporation = G("DateOfIncorporation"), DateOfCommencement = G("DateOfCommencement"),
            PlaceOfIncorporation = G("PlaceOfIncorporation"), CountryOfIncorporation = G("CountryOfIncorporation"),
            TinIssuingCountry = G("TinIssuingCountry"), Pan = G("Pan"), Form97 = G("Form97"),
            TinGstNumber = G("TinGstNumber"), PanDocument = G("PanDocument"), PanVerified = G("PanVerified"),
            TinGstnDocument = G("TinGstnDocument"),
        };
    }

    private static void Add(DbCommand cmd, string name, object? value)
        => cmd.Parameters.Add(NewParam(name, value));
}
