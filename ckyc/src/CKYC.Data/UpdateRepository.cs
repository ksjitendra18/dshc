using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;

namespace CKYC.Data;

/// <summary>
/// SQLite persistence for the bulk-update pipeline: JSON intake rows, per-client-type
/// batch claiming (search_request conventions), FVU audit and .UPD.RESm response import.
/// </summary>
public sealed class UpdateRepository : IUpdateRepository
{
    private readonly ICkycDatabase _db;

    public UpdateRepository(ICkycDatabase db) => _db = db;

    public async Task<UpdateIngestResult> InsertAsync(IReadOnlyList<UpdateRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return new UpdateIngestResult(0, 0);
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        var inserted = 0;
        foreach (var request in requests)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (DbTransaction)tx;
            cmd.CommandText = """
                INSERT INTO update_request
                    (ExternalRequestId, CustomerId, ClientType, CkycNumber,
                     RawRequestJson, ProcessingStatus, CreatedAt, UpdatedAt)
                VALUES
                    (@external, @customer, @client, @ckyc, @raw, 0, @now, @now)
                """;
            Add(cmd, "@external", request.ExternalRequestId);
            Add(cmd, "@customer", request.CustomerId);
            Add(cmd, "@client", request.ClientType);
            Add(cmd, "@ckyc", request.CkycNumber);
            Add(cmd, "@raw", request.RawRequestJson);
            Add(cmd, "@now", now);
            inserted += await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new UpdateIngestResult(inserted, requests.Count);
    }

    public async Task<UpdateClaim?> ClaimAsync(string clientType, int limit, DateOnly businessDate, int sequenceStart,
        TimeSpan claimTimeout, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var token = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(claimTimeout).ToString("o");
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Atomically flip pending (or stale-claimed) requests of this client type to claimed.
        await using (var claim = conn.CreateCommand())
        {
            claim.Transaction = (DbTransaction)tx;
            claim.CommandText = """
                UPDATE update_request
                   SET ProcessingStatus=1, ClaimToken=@token, ClaimedAt=@now, LastError=NULL, UpdatedAt=@now
                 WHERE Id IN (
                     SELECT Id FROM update_request
                      WHERE ClientType=@client
                        AND (ProcessingStatus=0 OR (ProcessingStatus=1 AND ClaimedAt < @stale))
                      ORDER BY Id LIMIT @limit
                 )
                """;
            Add(claim, "@token", token);
            Add(claim, "@client", clientType);
            Add(claim, "@now", now.ToString("o"));
            Add(claim, "@stale", staleBefore);
            Add(claim, "@limit", limit);
            if (await claim.ExecuteNonQueryAsync(ct) == 0)
            {
                await tx.RollbackAsync(ct);
                return null;
            }
        }

        // Daily sequence number for the .UPD file name — separate counter per client type.
        var date = businessDate.ToString("yyyy-MM-dd");
        var sequence = sequenceStart;
        await using (var seq = conn.CreateCommand())
        {
            seq.Transaction = (DbTransaction)tx;
            seq.CommandText = "SELECT MAX(FileSequence) FROM update_batch WHERE BusinessDate=@date AND ClientType=@client";
            Add(seq, "@date", date); Add(seq, "@client", clientType);
            var value = await seq.ExecuteScalarAsync(ct);
            if (value is not null && value is not DBNull) sequence = Math.Max(sequenceStart, Convert.ToInt32(value) + 1);
        }

        var records = await ReadClaimedAsync(conn, (DbTransaction)tx, token, ct);
        await using (var batch = conn.CreateCommand())
        {
            batch.Transaction = (DbTransaction)tx;
            batch.CommandText = """
                INSERT INTO update_batch
                    (BusinessDate, FileSequence, ClientType, ClaimToken, RecordCount, Status, CreatedAt)
                VALUES (@date, @sequence, @client, @token, @count, 1, @now)
                """;
            Add(batch, "@date", date); Add(batch, "@sequence", sequence); Add(batch, "@client", clientType);
            Add(batch, "@token", token); Add(batch, "@count", records.Count); Add(batch, "@now", now.ToString("o"));
            await batch.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new UpdateClaim(token, businessDate, sequence, clientType, records);
    }

    public async Task CompleteAsync(UpdateClaim claim, string batchKey, string fileName, string filePath,
        IReadOnlyDictionary<string, int> lineByCkycNumber, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        foreach (var record in claim.Records)
        {
            lineByCkycNumber.TryGetValue(record.CkycNumber, out var line);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (DbTransaction)tx;
            cmd.CommandText = """
                UPDATE update_request
                   SET ProcessingStatus=2, ProcessedAt=@now, OutputFileName=@file,
                       OutputLineNumber=@line, OutputBatchKey=@batch, UpdatedAt=@now
                 WHERE Id=@id AND ClaimToken=@token AND ProcessingStatus=1
                """;
            Add(cmd, "@now", now); Add(cmd, "@file", fileName); Add(cmd, "@line", line);
            Add(cmd, "@batch", batchKey); Add(cmd, "@id", record.Id); Add(cmd, "@token", claim.Token);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (DbTransaction)tx;
            cmd.CommandText = """
                UPDATE update_batch SET Status=2, FileName=@file, FilePath=@path, CompletedAt=@now
                 WHERE ClaimToken=@token
                """;
            Add(cmd, "@file", fileName); Add(cmd, "@path", filePath); Add(cmd, "@now", now); Add(cmd, "@token", claim.Token);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task FailAsync(UpdateClaim claim, string failureMessage, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        await ExecuteAsync(conn, (DbTransaction)tx,
            "UPDATE update_request SET ProcessingStatus=3, LastError=@error, UpdatedAt=@now WHERE ClaimToken=@token AND ProcessingStatus=1",
            claim.Token, failureMessage, now, ct);
        await ExecuteAsync(conn, (DbTransaction)tx,
            "UPDATE update_batch SET Status=3, Error=@error, CompletedAt=@now WHERE ClaimToken=@token",
            claim.Token, failureMessage, now, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SkipAsync(string claimToken, IReadOnlyDictionary<long, string> errorsByRequestId, CancellationToken ct = default)
    {
        if (errorsByRequestId.Count == 0) return;
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var (requestId, error) in errorsByRequestId)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (DbTransaction)tx;
            cmd.CommandText = """
                UPDATE update_request
                   SET ProcessingStatus=3, LastError=@error, UpdatedAt=@now
                 WHERE Id=@id AND ClaimToken=@token AND ProcessingStatus=1
                """;
            Add(cmd, "@error", error); Add(cmd, "@now", DateTime.UtcNow.ToString("o"));
            Add(cmd, "@id", requestId); Add(cmd, "@token", claimToken);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<UpdateGeneratedBatch?> GetGeneratedBatchAsync(string? fileName, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = fileName is null
            ? "SELECT * FROM update_batch WHERE Status=2 ORDER BY Id DESC LIMIT 1"
            : "SELECT * FROM update_batch WHERE FileName=@file AND Status=2 ORDER BY Id DESC LIMIT 1";
        if (fileName is not null) Add(cmd, "@file", fileName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var path = Text(reader, "FilePath");
        var name = Text(reader, "FileName");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name)) return null;
        return new UpdateGeneratedBatch(Convert.ToInt64(reader["Id"]), name, path, Convert.ToInt32(reader["RecordCount"]));
    }

    public async Task RecordFvuAsync(long batchId, bool passed, string? zipPath, string? hash, string? failureMessage, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE update_batch
               SET Status=@status, FvuZipPath=@zip, FvuHash=@hash, Error=@error, CompletedAt=@now
             WHERE Id=@id
            """;
        Add(cmd, "@status", passed ? 4 : 5);
        Add(cmd, "@zip", zipPath); Add(cmd, "@hash", hash); Add(cmd, "@error", failureMessage);
        Add(cmd, "@now", DateTime.UtcNow.ToString("o")); Add(cmd, "@id", batchId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<UpdateResponseImportResult> ImportResponseAsync(UpdateResponseImport response, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var localTx = (DbTransaction)tx;

        // SHA-256 duplicate guard so re-running `update-response` never double-imports.
        await using (var duplicate = conn.CreateCommand())
        {
            duplicate.Transaction = localTx;
            duplicate.CommandText = "SELECT COUNT(1) FROM update_response_file WHERE SourceHash=@hash";
            Add(duplicate, "@hash", response.SourceHash);
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(ct)) > 0)
            {
                await tx.RollbackAsync(ct);
                return new UpdateResponseImportResult(0, 0, true);
            }
        }

        long? batchId = null;
        string? batchClientType = null;
        await using (var batch = conn.CreateCommand())
        {
            batch.Transaction = localTx;
            batch.CommandText = "SELECT Id, ClientType FROM update_batch WHERE FileName=@file ORDER BY Id DESC LIMIT 1";
            Add(batch, "@file", response.InputFileName);
            await using var reader = await batch.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                batchId = Convert.ToInt64(reader["Id"]);
                batchClientType = Convert.ToString(reader["ClientType"]);
            }
        }

        var now = DateTime.UtcNow.ToString("o");
        await using (var header = conn.CreateCommand())
        {
            header.Transaction = localTx;
            header.CommandText = """
                INSERT INTO update_response_file
                    (UpdateBatchId, ResponseFileName, ResponseFileNumber, ClientType, FiCode, RegionCode,
                     TotalRecords, TotalProcessed, RecordsUnderProcessing, RecordsFailed,
                     ResponseTimestamp, Filler1, Filler2, RawHeaderData, SourceArchiveName, SourceHash, CreatedAt)
                VALUES (@batch, @file, @number, @client, @fi, @region, @total, @processed, @under,
                        @failed, @timestamp, @filler1, @filler2, @raw, @archive, @hash, @now)
                """;
            Add(header, "@batch", batchId); Add(header, "@file", response.Header.ResponseFileName);
            Add(header, "@number", response.Header.ResponseFileNumber); Add(header, "@client", response.Header.ClientType ?? batchClientType);
            Add(header, "@fi", response.Header.FiCode); Add(header, "@region", response.Header.RegionCode);
            Add(header, "@total", response.Header.TotalRecords); Add(header, "@processed", response.Header.TotalProcessed);
            Add(header, "@under", response.Header.RecordsUnderProcessing); Add(header, "@failed", response.Header.RecordsFailed);
            Add(header, "@timestamp", response.Header.ResponseTimestamp); Add(header, "@filler1", response.Header.Filler1);
            Add(header, "@filler2", response.Header.Filler2); Add(header, "@raw", response.Header.RawHeaderData);
            Add(header, "@archive", response.SourceArchiveName); Add(header, "@hash", response.SourceHash); Add(header, "@now", now);
            await header.ExecuteNonQueryAsync(ct);
        }

        var matched = 0;
        foreach (var detail in response.Details)
        {
            long? requestId = null;
            if (detail.InputRecord20LineNumber is not null)
            {
                await using var request = conn.CreateCommand();
                request.Transaction = localTx;
                request.CommandText = """
                    SELECT Id FROM update_request
                     WHERE OutputFileName=@file AND OutputLineNumber=@line
                     ORDER BY Id DESC LIMIT 1
                    """;
                Add(request, "@file", response.InputFileName); Add(request, "@line", detail.InputRecord20LineNumber);
                var value = await request.ExecuteScalarAsync(ct);
                if (value is not null && value is not DBNull) requestId = Convert.ToInt64(value);
            }

            await InsertResponseDetailAsync(conn, localTx, response.Header, detail, requestId, now, ct);
            if (requestId is null) continue;
            matched++;

            // Record status mapping (Update_response sheets): 02 No Match / 03 Rejected.
            var statusName = detail.RecordStatus switch
            {
                "02" => "No Match",
                "03" => "Rejected",
                _ => detail.RecordStatus ?? string.Empty,
            };
            await using var update = conn.CreateCommand();
            update.Transaction = localTx;
            update.CommandText = """
                UPDATE update_request
                   SET ResponseStatus=@status, LastAckNumber=@ack, LastResponseStatusCode=@code,
                       LastResponseRemark=@remark, ResponseReadAt=@now, UpdatedAt=@now
                 WHERE Id=@id
                """;
            Add(update, "@status", statusName); Add(update, "@ack", detail.AckNumber);
            Add(update, "@code", detail.RecordStatus); Add(update, "@remark",
                string.IsNullOrWhiteSpace(detail.RejectionRemark) ? detail.CkycNumber : detail.RejectionRemark);
            Add(update, "@now", now); Add(update, "@id", requestId);
            await update.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new UpdateResponseImportResult(response.Details.Count, matched, false);
    }

    private static async Task InsertResponseDetailAsync(DbConnection conn, DbTransaction tx, UpdateResponseHeader header,
        UpdateResponseDetail detail, long? requestId, string now, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO update_response
                (UpdateRequestId, ResponseFileName, ResponseFileNumber, LineNumber, InputRecord20LineNumber,
                 AckNumber, RecordStatus, CkycNumber, RejectionRemark, RawResponseData, CreatedAt)
            VALUES (@request, @file, @number, @line, @inputLine, @ack, @status, @ckyc, @remark, @raw, @now)
            """;
        Add(cmd, "@request", requestId); Add(cmd, "@file", header.ResponseFileName); Add(cmd, "@number", header.ResponseFileNumber);
        Add(cmd, "@line", detail.LineNumber); Add(cmd, "@inputLine", detail.InputRecord20LineNumber);
        Add(cmd, "@ack", detail.AckNumber); Add(cmd, "@status", detail.RecordStatus);
        Add(cmd, "@ckyc", detail.CkycNumber); Add(cmd, "@remark", detail.RejectionRemark);
        Add(cmd, "@raw", detail.RawResponseData); Add(cmd, "@now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteAsync(DbConnection conn, DbTransaction tx, string sql,
        string token, string failureMessage, string now, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx; cmd.CommandText = sql;
        Add(cmd, "@token", token); Add(cmd, "@error", failureMessage); Add(cmd, "@now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Rehydrates claimed rows; RawRequestJson is re-parsed by the processor at write time.</summary>
    private static async Task<IReadOnlyList<UpdateRequest>> ReadClaimedAsync(
        DbConnection conn, DbTransaction tx, string token, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT * FROM update_request WHERE ClaimToken=@token AND ProcessingStatus=1 ORDER BY Id";
        Add(cmd, "@token", token);
        var result = new List<UpdateRequest>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new UpdateRequest
            {
                Id = Convert.ToInt64(r["Id"]),
                ExternalRequestId = Text(r, "ExternalRequestId"),
                CustomerId = Text(r, "CustomerId"),
                ClientType = Text(r, "ClientType") ?? "I",
                CkycNumber = Text(r, "CkycNumber") ?? string.Empty,
                RawRequestJson = Text(r, "RawRequestJson"),
                ProcessingStatus = Convert.ToInt32(r["ProcessingStatus"]),
                ClaimToken = Text(r, "ClaimToken"),
                ClaimedAt = Date(r, "ClaimedAt"),
            });
        }
        return result;
    }

    private static string? Text(DbDataReader reader, string name) => reader[name] is DBNull ? null : Convert.ToString(reader[name]);
    private static DateTime? Date(DbDataReader reader, string name) => DateTime.TryParse(Text(reader, name), out var value) ? value : null;
    private static void Add(DbCommand command, string name, object? value) => command.Parameters.Add(MasterRepository.NewParam(name, value));
}
