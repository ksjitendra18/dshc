using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using Microsoft.EntityFrameworkCore;
using DownloadResponseArtifactEntity = CKYC.Data.Entities.DownloadResponseArtifact;
using DownloadResponseFileEntity = CKYC.Data.Entities.DownloadResponseFile;
using DownloadResponseLineEntity = CKYC.Data.Entities.DownloadResponseLine;

namespace CKYC.Data;

/// <summary>EF Core (SQL Server) store for immutable CKYCR download response snapshots.</summary>
public sealed class DownloadRepository : IDownloadRepository
{
    private readonly ICkycDatabase _db;

    public DownloadRepository(ICkycDatabase db) => _db = db;

    public async Task<DownloadImportResult> ImportAsync(DownloadResponseImport response, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync(
            $"CKYC:download-response:{response.SourceHash}:{response.ResponseFileName}", ct);

        var duplicate = await db.DownloadResponseFiles
            .AnyAsync(f => f.SourceHash == response.SourceHash && f.ResponseFileName == response.ResponseFileName, ct);
        if (duplicate)
        {
            await tx.RollbackAsync(ct);
            return new DownloadImportResult(0, 0, true);
        }

        var now = DateTime.UtcNow;
        var file = new DownloadResponseFileEntity
        {
            ResponseFileName = response.ResponseFileName,
            ResponseFileNumber = response.ResponseFileNumber,
            FiCode = response.FiCode,
            RegionCode = response.RegionCode,
            ClientType = response.ClientType,
            TotalRecords = response.TotalRecords,
            Version = response.Version,
            ResponseDate = response.ResponseDate,
            RawHeaderData = response.RawHeaderData,
            SourceArchiveName = response.SourceArchiveName,
            SourceHash = response.SourceHash,
            CreatedAt = now,
        };
        db.DownloadResponseFiles.Add(file);
        await db.SaveChangesAsync(ct);
        var fileId = file.Id;

        foreach (var line in response.Lines)
        {
            db.DownloadResponseLines.Add(new DownloadResponseLineEntity
            {
                DownloadResponseFileId = fileId,
                SourceEntryPath = line.SourceEntryPath,
                RecordType = line.RecordType,
                LineNumber = line.LineNumber,
                InputRecord20LineNumber = line.InputRecord20LineNumber,
                CkycNumber = line.CkycNumber,
                RawData = line.RawData,
                CreatedAt = now,
            });
        }

        foreach (var artifact in response.Artifacts)
        {
            db.DownloadResponseArtifacts.Add(new DownloadResponseArtifactEntity
            {
                DownloadResponseFileId = fileId,
                EntryPath = artifact.EntryPath,
                FileName = artifact.FileName,
                Size = artifact.Size,
                Sha256 = artifact.Sha256,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DownloadImportResult(response.Lines.Count, response.Artifacts.Count, false);
    }
}
