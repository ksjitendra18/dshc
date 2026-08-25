using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;

namespace CKYC.Data;

/// <summary>Persists search requests and claims batches transactionally.</summary>
public sealed class SearchRepository : ISearchRepository
{
    private readonly ICkycDatabase _db;

    public SearchRepository(ICkycDatabase db) => _db = db;

    public async Task<SearchIngestResult> InsertAsync(IReadOnlyList<SearchRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return new SearchIngestResult(0, 0);
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        var inserted = 0;
        foreach (var request in requests)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (DbTransaction)tx;
            cmd.CommandText = """
                INSERT INTO search_request
                    (ExternalRequestId, CustomerId, ClientType, SearchOption,
                     IdentityTypeAndNumber, FirstName, MiddleName, LastName, DateOfBirth,
                     LegalEntityName, DateOfIncorporation, Gender, PhotoReferenceNumber,
                     Relation, RelationFirstName, RelationMiddleName, RelationLastName,
                     MobileNumber, VerifiableCredential, Constitution, RawRequestJson,
                     ProcessingStatus, CreatedAt, UpdatedAt)
                VALUES
                    (@external, @customer, @client, @option, @identity, @first, @middle,
                     @last, @dob, @legal, @doi, @gender, @photo, @relation, @rfirst,
                     @rmiddle, @rlast, @mobile, @credential, @constitution, @raw, 0, @now, @now)
                """;
            Add(cmd, "@external", request.ExternalRequestId);
            Add(cmd, "@customer", request.CustomerId);
            Add(cmd, "@client", request.ClientType);
            Add(cmd, "@option", request.SearchOption);
            Add(cmd, "@identity", request.IdentityTypeAndNumber);
            Add(cmd, "@first", request.FirstName);
            Add(cmd, "@middle", request.MiddleName);
            Add(cmd, "@last", request.LastName);
            Add(cmd, "@dob", request.DateOfBirth);
            Add(cmd, "@legal", request.LegalEntityName);
            Add(cmd, "@doi", request.DateOfIncorporation);
            Add(cmd, "@gender", request.Gender);
            Add(cmd, "@photo", request.PhotoReferenceNumber);
            Add(cmd, "@relation", request.Relation);
            Add(cmd, "@rfirst", request.RelationFirstName);
            Add(cmd, "@rmiddle", request.RelationMiddleName);
            Add(cmd, "@rlast", request.RelationLastName);
            Add(cmd, "@mobile", request.MobileNumber);
            Add(cmd, "@credential", request.VerifiableCredential);
            Add(cmd, "@constitution", request.Constitution);
            Add(cmd, "@raw", request.RawRequestJson);
            Add(cmd, "@now", now);
            inserted += await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new SearchIngestResult(inserted, requests.Count);
    }

    public async Task<SearchClaim?> ClaimAsync(int limit, DateOnly businessDate, int sequenceStart,
        TimeSpan claimTimeout, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var token = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(claimTimeout).ToString("o");
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var claim = conn.CreateCommand())
        {
            claim.Transaction = (DbTransaction)tx;
            claim.CommandText = """
                UPDATE search_request
                   SET ProcessingStatus=1, ClaimToken=@token, ClaimedAt=@now,
                       LastError=NULL, UpdatedAt=@now
                 WHERE Id IN (
                    SELECT Id FROM search_request
                     WHERE ProcessingStatus=0
                        OR (ProcessingStatus=1 AND ClaimedAt < @stale)
                     ORDER BY Id LIMIT @limit
                 )
                """;
            Add(claim, "@token", token);
            Add(claim, "@now", now.ToString("o"));
            Add(claim, "@stale", staleBefore);
            Add(claim, "@limit", limit);
            if (await claim.ExecuteNonQueryAsync(ct) == 0)
            {
                await tx.RollbackAsync(ct);
                return null;
            }
        }

        var date = businessDate.ToString("yyyy-MM-dd");
        var sequence = sequenceStart;
        await using (var seq = conn.CreateCommand())
        {
            seq.Transaction = (DbTransaction)tx;
            seq.CommandText = "SELECT MAX(FileSequence) FROM search_batch WHERE BusinessDate=@date";
            Add(seq, "@date", date);
            var value = await seq.ExecuteScalarAsync(ct);
            if (value is not null && value is not DBNull) sequence = Math.Max(sequenceStart, Convert.ToInt32(value) + 1);
        }

        var records = await ReadClaimAsync(conn, (DbTransaction)tx, token, ct);
        await using (var batch = conn.CreateCommand())
        {
            batch.Transaction = (DbTransaction)tx;
            batch.CommandText = """
                INSERT INTO search_batch
                    (BusinessDate, FileSequence, ClaimToken, RecordCount, Status, CreatedAt)
                VALUES (@date, @sequence, @token, @count, 1, @now)
                """;
            Add(batch, "@date", date);
            Add(batch, "@sequence", sequence);
            Add(batch, "@token", token);
            Add(batch, "@count", records.Count);
            Add(batch, "@now", now.ToString("o"));
            await batch.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new SearchClaim(token, sequence, records);
    }

    public async Task CompleteAsync(SearchClaim claim, string fileName, string filePath, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        for (var index = 0; index < claim.Records.Count; index++)
        {
            var record = claim.Records[index];
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (DbTransaction)tx;
            cmd.CommandText = """
                UPDATE search_request
                   SET ProcessingStatus=2, ProcessedAt=@now, OutputFileName=@file,
                       OutputLineNumber=@line,
                       UpdatedAt=@now
                 WHERE Id=@id AND ClaimToken=@token AND ProcessingStatus=1
                """;
            Add(cmd, "@now", now); Add(cmd, "@file", fileName); Add(cmd, "@line", index + 1);
            Add(cmd, "@id", record.Id); Add(cmd, "@token", claim.Token);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (DbTransaction)tx;
            cmd.CommandText = """
                UPDATE search_batch SET Status=2, FileName=@file, FilePath=@path, CompletedAt=@now
                 WHERE ClaimToken=@token
                """;
            Add(cmd, "@file", fileName); Add(cmd, "@path", filePath);
            Add(cmd, "@now", now); Add(cmd, "@token", claim.Token);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task FailAsync(SearchClaim claim, string failureMessage, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        await ExecuteAsync(conn, (DbTransaction)tx,
            "UPDATE search_request SET ProcessingStatus=3, LastError=@error, UpdatedAt=@now WHERE ClaimToken=@token AND ProcessingStatus=1",
            claim.Token, failureMessage, now, ct);
        await ExecuteAsync(conn, (DbTransaction)tx,
            "UPDATE search_batch SET Status=3, Error=@error, CompletedAt=@now WHERE ClaimToken=@token",
            claim.Token, failureMessage, now, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<SearchGeneratedBatch?> GetGeneratedBatchAsync(string? fileName, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = fileName is null
            ? "SELECT * FROM search_batch WHERE Status=2 ORDER BY Id DESC LIMIT 1"
            : "SELECT * FROM search_batch WHERE FileName=@file AND Status=2 ORDER BY Id DESC LIMIT 1";
        if (fileName is not null) Add(cmd, "@file", fileName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var path = Text(reader, "FilePath");
        var name = Text(reader, "FileName");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name)) return null;
        return new SearchGeneratedBatch(Convert.ToInt64(reader["Id"]), name, path, Convert.ToInt32(reader["RecordCount"]));
    }

    public async Task RecordFvuAsync(long batchId, bool passed, string? zipPath, string? hash, string? failureMessage, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE search_batch
               SET Status=@status, FvuZipPath=@zip, FvuHash=@hash, Error=@error, CompletedAt=@now
             WHERE Id=@id
            """;
        Add(cmd, "@status", passed ? 4 : 5);
        Add(cmd, "@zip", zipPath);
        Add(cmd, "@hash", hash);
        Add(cmd, "@error", failureMessage);
        Add(cmd, "@now", DateTime.UtcNow.ToString("o"));
        Add(cmd, "@id", batchId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SearchResponseImportResult> ImportResponseAsync(SearchResponseImport response, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var localTx = (DbTransaction)tx;
        await using (var duplicate = conn.CreateCommand())
        {
            duplicate.Transaction = localTx;
            duplicate.CommandText = "SELECT COUNT(1) FROM search_response_file WHERE SourceHash=@hash";
            Add(duplicate, "@hash", response.SourceHash);
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(ct)) > 0)
            {
                await tx.RollbackAsync(ct);
                return new SearchResponseImportResult(0, 0, true);
            }
        }

        long? searchBatchId = null;
        await using (var batch = conn.CreateCommand())
        {
            batch.Transaction = localTx;
            batch.CommandText = "SELECT Id FROM search_batch WHERE FileName=@file ORDER BY Id DESC LIMIT 1";
            Add(batch, "@file", response.InputFileName);
            var value = await batch.ExecuteScalarAsync(ct);
            if (value is not null && value is not DBNull) searchBatchId = Convert.ToInt64(value);
        }

        var now = DateTime.UtcNow.ToString("o");
        await using (var header = conn.CreateCommand())
        {
            header.Transaction = localTx;
            header.CommandText = """
                INSERT INTO search_response_file
                    (SearchBatchId, ResponseFileName, ResponseFileNumber, FiCode, RegionCode,
                     TotalRecords, TotalProcessed, RecordsUnderProcessing, RecordsFailed,
                     ResponseTimestamp, Filler, RawHeaderData, SourceArchiveName, SourceHash, CreatedAt)
                VALUES (@batch, @file, @number, @fi, @region, @total, @processed, @under,
                        @failed, @timestamp, @filler, @raw, @archive, @hash, @now)
                """;
            Add(header, "@batch", searchBatchId); Add(header, "@file", response.Header.ResponseFileName);
            Add(header, "@number", response.Header.ResponseFileNumber); Add(header, "@fi", response.Header.FiCode);
            Add(header, "@region", response.Header.RegionCode); Add(header, "@total", response.Header.TotalRecords);
            Add(header, "@processed", response.Header.TotalProcessed); Add(header, "@under", response.Header.RecordsUnderProcessing);
            Add(header, "@failed", response.Header.RecordsFailed); Add(header, "@timestamp", response.Header.ResponseTimestamp);
            Add(header, "@filler", response.Header.Filler); Add(header, "@raw", response.Header.RawHeaderData);
            Add(header, "@archive", response.SourceArchiveName); Add(header, "@hash", response.SourceHash); Add(header, "@now", now);
            await header.ExecuteNonQueryAsync(ct);
        }

        var matched = 0;
        foreach (var detail in response.Details)
        {
            long? requestId = null;
            if (detail.InputRecordLineNumber is not null)
            {
                await using var request = conn.CreateCommand();
                request.Transaction = localTx;
                request.CommandText = "SELECT Id FROM search_request WHERE OutputFileName=@file AND OutputLineNumber=@line ORDER BY Id DESC LIMIT 1";
                Add(request, "@file", response.InputFileName); Add(request, "@line", detail.InputRecordLineNumber);
                var value = await request.ExecuteScalarAsync(ct);
                if (value is not null && value is not DBNull) requestId = Convert.ToInt64(value);
            }

            await InsertResponseDetailAsync(conn, localTx, response.Header, detail, requestId, now, ct);
            if (requestId is null) continue;
            matched++;
            await using var update = conn.CreateCommand();
            update.Transaction = localTx;
            update.CommandText = """
                UPDATE search_request
                   SET ResponseStatus='ResponseRead', LastSearchKey=@key, LastCkycReference=@reference,
                       LastResponseRemark=@remark, ResponseReadAt=@now, UpdatedAt=@now
                 WHERE Id=@id
                """;
            Add(update, "@key", detail.SearchKey); Add(update, "@reference", detail.CkycReferenceNumber);
            Add(update, "@remark", detail.Remark); Add(update, "@now", now); Add(update, "@id", requestId);
            await update.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new SearchResponseImportResult(response.Details.Count, matched, false);
    }

    private static async Task InsertResponseDetailAsync(DbConnection conn, DbTransaction tx, SearchResponseHeader header,
        SearchResponseDetail detail, long? requestId, string now, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO search_response
                (SearchRequestId, ResponseFileName, ResponseFileNumber, LineNumber, InputRecordLineNumber,
                 ClientType, SearchByOvdType, SearchByOvdNumber, SearchKey, CkycReferenceNumber,
                 FirstName, MiddleName, LastName, Gender, MobileNumber, EmailAddress, LastUpdatedDate,
                 Cin, LegalEntityName, PhotoReference, RegistrationDate, DeactivationReason, Remark,
                 PanDocument, AadhaarDocument, PassportDocument, DrivingLicenseDocument, VoterIdDocument,
                 NregaDocument, DisabilityDocument, Form6061Document, ForeignJurisdictionDocument, NprDocument,
                 UtilityBillDocument, IncorporationDocument, MemorandumDocument, RegistrationCertificate,
                 PartnershipDeed, TrustDeed, SupportingPoiDocument, OtherDocument, Filler1, Filler2, Filler3,
                 Filler4, Filler5, Filler6, Filler7, Filler8, RecordLevelHash, RawResponseData, CreatedAt)
            VALUES (@request, @file, @number, @line, @inputLine, @client, @ovdType, @ovdNumber, @key, @reference,
                    @first, @middle, @last, @gender, @mobile, @email, @updated, @cin, @legal, @photo, @registration,
                    @deactivation, @remark, @pan, @aadhaar, @passport, @dl, @voter, @nrega, @disability, @form,
                    @foreign, @npr, @utility, @incorporation, @memorandum, @certificate, @partnership, @trust,
                    @supporting, @other, @f1, @f2, @f3, @f4, @f5, @f6, @f7, @f8, @hash, @raw, @now)
            """;
        Add(cmd, "@request", requestId); Add(cmd, "@file", header.ResponseFileName); Add(cmd, "@number", header.ResponseFileNumber);
        Add(cmd, "@line", detail.LineNumber); Add(cmd, "@inputLine", detail.InputRecordLineNumber); Add(cmd, "@client", detail.ClientType);
        Add(cmd, "@ovdType", detail.SearchByOvdType); Add(cmd, "@ovdNumber", detail.SearchByOvdNumber); Add(cmd, "@key", detail.SearchKey);
        Add(cmd, "@reference", detail.CkycReferenceNumber); Add(cmd, "@first", detail.FirstName); Add(cmd, "@middle", detail.MiddleName);
        Add(cmd, "@last", detail.LastName); Add(cmd, "@gender", detail.Gender); Add(cmd, "@mobile", detail.MobileNumber);
        Add(cmd, "@email", detail.EmailAddress); Add(cmd, "@updated", detail.LastUpdatedDate); Add(cmd, "@cin", detail.Cin);
        Add(cmd, "@legal", detail.LegalEntityName); Add(cmd, "@photo", detail.PhotoReference); Add(cmd, "@registration", detail.RegistrationDate);
        Add(cmd, "@deactivation", detail.DeactivationReason); Add(cmd, "@remark", detail.Remark);
        var flags = detail.DocumentFlags; var fillers = detail.Fillers;
        foreach (var (name, value) in new[] { ("@pan", At(flags, 0)), ("@aadhaar", At(flags, 1)), ("@passport", At(flags, 2)), ("@dl", At(flags, 3)), ("@voter", At(flags, 4)), ("@nrega", At(flags, 5)), ("@disability", At(flags, 6)), ("@form", At(flags, 7)), ("@foreign", At(flags, 8)), ("@npr", At(flags, 9)), ("@utility", At(flags, 10)), ("@incorporation", At(flags, 11)), ("@memorandum", At(flags, 12)), ("@certificate", At(flags, 13)), ("@partnership", At(flags, 14)), ("@trust", At(flags, 15)), ("@supporting", At(flags, 16)), ("@other", At(flags, 17)), ("@f1", At(fillers, 0)), ("@f2", At(fillers, 1)), ("@f3", At(fillers, 2)), ("@f4", At(fillers, 3)), ("@f5", At(fillers, 4)), ("@f6", At(fillers, 5)), ("@f7", At(fillers, 6)), ("@f8", At(fillers, 7)) }) Add(cmd, name, value);
        Add(cmd, "@hash", detail.RecordLevelHash); Add(cmd, "@raw", detail.RawResponseData); Add(cmd, "@now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string? At(string?[] values, int index) => index < values.Length ? values[index] : null;

    private static async Task ExecuteAsync(DbConnection conn, DbTransaction tx, string sql,
        string token, string failureMessage, string now, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx; cmd.CommandText = sql;
        Add(cmd, "@token", token); Add(cmd, "@error", failureMessage); Add(cmd, "@now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<SearchRequest>> ReadClaimAsync(
        DbConnection conn, DbTransaction tx, string token, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT * FROM search_request WHERE ClaimToken=@token ORDER BY Id";
        Add(cmd, "@token", token);
        var result = new List<SearchRequest>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new SearchRequest
            {
                Id = Convert.ToInt64(r["Id"]), ExternalRequestId = Text(r, "ExternalRequestId"),
                CustomerId = Text(r, "CustomerId"), ClientType = Text(r, "ClientType") ?? "I",
                SearchOption = Convert.ToInt32(r["SearchOption"]), IdentityTypeAndNumber = Text(r, "IdentityTypeAndNumber"),
                FirstName = Text(r, "FirstName"), MiddleName = Text(r, "MiddleName"), LastName = Text(r, "LastName"),
                DateOfBirth = Text(r, "DateOfBirth"), LegalEntityName = Text(r, "LegalEntityName"),
                DateOfIncorporation = Text(r, "DateOfIncorporation"), Gender = Text(r, "Gender"),
                PhotoReferenceNumber = Text(r, "PhotoReferenceNumber"), Relation = Text(r, "Relation"),
                RelationFirstName = Text(r, "RelationFirstName"), RelationMiddleName = Text(r, "RelationMiddleName"),
                RelationLastName = Text(r, "RelationLastName"), MobileNumber = Text(r, "MobileNumber"),
                VerifiableCredential = Text(r, "VerifiableCredential"), Constitution = Text(r, "Constitution"),
                RawRequestJson = Text(r, "RawRequestJson"), ProcessingStatus = Convert.ToInt32(r["ProcessingStatus"]),
                ClaimToken = Text(r, "ClaimToken"), ClaimedAt = Date(r, "ClaimedAt")
            });
        }
        return result;
    }

    private static string? Text(DbDataReader reader, string name) => reader[name] is DBNull ? null : Convert.ToString(reader[name]);
    private static DateTime? Date(DbDataReader reader, string name) => DateTime.TryParse(Text(reader, name), out var value) ? value : null;
    private static void Add(DbCommand command, string name, object? value) => command.Parameters.Add(MasterRepository.NewParam(name, value));
}
