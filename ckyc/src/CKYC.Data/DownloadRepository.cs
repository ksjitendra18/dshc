using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using static CKYC.Data.MasterRepository;

namespace CKYC.Data;

public sealed class DownloadRepository : IDownloadRepository
{
    private readonly ICkycDatabase _db;
    public DownloadRepository(ICkycDatabase db) => _db = db;

    public async Task<DownloadImportResult> ImportAsync(DownloadResponseImport response, CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        await using var tx = await conn.BeginTransactionAsync(ct);
        var localTx = (DbTransaction)tx;

        await using (var duplicate = conn.CreateCommand())
        {
            duplicate.Transaction = localTx;
            duplicate.CommandText = "SELECT COUNT(1) FROM download_response_file WHERE SourceHash=@hash AND ResponseFileName=@name";
            Add(duplicate, "@hash", response.SourceHash); Add(duplicate, "@name", response.ResponseFileName);
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(ct)) > 0)
            {
                await tx.RollbackAsync(ct);
                return new DownloadImportResult(0, 0, true);
            }
        }

        var now = DateTime.UtcNow.ToString("o");
        long fileId;
        await using (var file = conn.CreateCommand())
        {
            file.Transaction = localTx;
            file.CommandText = """
                INSERT INTO download_response_file
                    (ResponseFileName, ResponseFileNumber, FiCode, RegionCode, ClientType, TotalRecords,
                     Version, ResponseDate, RawHeaderData, SourceArchiveName, SourceHash, CreatedAt)
                VALUES (@name,@number,@fi,@region,@client,@total,@version,@date,@raw,@archive,@hash,@now);
                SELECT last_insert_rowid();
                """;
            Add(file, "@name", response.ResponseFileName); Add(file, "@number", response.ResponseFileNumber);
            Add(file, "@fi", response.FiCode); Add(file, "@region", response.RegionCode); Add(file, "@client", response.ClientType);
            Add(file, "@total", response.TotalRecords); Add(file, "@version", response.Version); Add(file, "@date", response.ResponseDate);
            Add(file, "@raw", response.RawHeaderData); Add(file, "@archive", response.SourceArchiveName);
            Add(file, "@hash", response.SourceHash); Add(file, "@now", now);
            fileId = Convert.ToInt64(await file.ExecuteScalarAsync(ct));
        }

        foreach (var line in response.Lines)
        {
            await using var cmd = conn.CreateCommand(); cmd.Transaction = localTx;
            cmd.CommandText = """
                INSERT INTO download_response_line
                    (DownloadResponseFileId,SourceEntryPath,RecordType,LineNumber,InputRecord20LineNumber,CkycNumber,RawData,CreatedAt)
                VALUES (@file,@entry,@type,@line,@input,@ckyc,@raw,@now)
                """;
            Add(cmd, "@file", fileId); Add(cmd, "@entry", line.SourceEntryPath);
            Add(cmd, "@type", line.RecordType); Add(cmd, "@line", line.LineNumber);
            Add(cmd, "@input", line.InputRecord20LineNumber); Add(cmd, "@ckyc", line.CkycNumber);
            Add(cmd, "@raw", line.RawData); Add(cmd, "@now", now); await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var artifact in response.Artifacts)
        {
            await using var cmd = conn.CreateCommand(); cmd.Transaction = localTx;
            cmd.CommandText = """
                INSERT INTO download_response_artifact
                    (DownloadResponseFileId,EntryPath,FileName,Size,Sha256,CreatedAt)
                VALUES (@file,@path,@name,@size,@hash,@now)
                """;
            Add(cmd, "@file", fileId); Add(cmd, "@path", artifact.EntryPath); Add(cmd, "@name", artifact.FileName);
            Add(cmd, "@size", artifact.Size); Add(cmd, "@hash", artifact.Sha256); Add(cmd, "@now", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new DownloadImportResult(response.Lines.Count, response.Artifacts.Count, false);
    }

    private static void Add(DbCommand cmd, string name, object? value) => cmd.Parameters.Add(NewParam(name, value));
}
