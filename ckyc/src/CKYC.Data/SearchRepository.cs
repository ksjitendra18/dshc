using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using Microsoft.EntityFrameworkCore;
using SearchBatchEntity = CKYC.Data.Entities.SearchBatch;
using SearchRequestEntity = CKYC.Data.Entities.SearchRequest;
using SearchResponseEntity = CKYC.Data.Entities.SearchResponse;
using SearchResponseFileEntity = CKYC.Data.Entities.SearchResponseFile;

namespace CKYC.Data;

/// <summary>Persists search requests and claims batches transactionally (EF Core / SQL Server).</summary>
public sealed class SearchRepository : ISearchRepository
{
    private readonly ICkycDatabase _db;

    public SearchRepository(ICkycDatabase db) => _db = db;

    public async Task<SearchIngestResult> InsertAsync(IReadOnlyList<SearchRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return new SearchIngestResult(0, 0);
        await using var db = _db.CreateContext();
        var now = DateTime.UtcNow;
        foreach (var request in requests)
        {
            db.SearchRequests.Add(new SearchRequestEntity
            {
                ExternalRequestId = request.ExternalRequestId,
                CustomerId = request.CustomerId,
                ClientType = request.ClientType,
                SearchOption = request.SearchOption,
                IdentityTypeAndNumber = request.IdentityTypeAndNumber,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                LastName = request.LastName,
                DateOfBirth = request.DateOfBirth,
                LegalEntityName = request.LegalEntityName,
                DateOfIncorporation = request.DateOfIncorporation,
                Gender = request.Gender,
                PhotoReferenceNumber = request.PhotoReferenceNumber,
                Relation = request.Relation,
                RelationFirstName = request.RelationFirstName,
                RelationMiddleName = request.RelationMiddleName,
                RelationLastName = request.RelationLastName,
                MobileNumber = request.MobileNumber,
                VerifiableCredential = request.VerifiableCredential,
                Constitution = request.Constitution,
                RawRequestJson = request.RawRequestJson,
                ProcessingStatus = 0,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
        return new SearchIngestResult(requests.Count, requests.Count);
    }

    public async Task<SearchClaim?> ClaimAsync(int limit, DateOnly businessDate, int sequenceStart,
        TimeSpan claimTimeout, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var token = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(claimTimeout);

        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync("CKYC:search-claim", ct);

        // The transaction-scoped SQL Server application lock serializes the short claim and
        // daily-sequence allocation window across processes.
        var claimIds = await db.SearchRequests
            .Where(r => r.ProcessingStatus == 0 || (r.ProcessingStatus == 1 && r.ClaimedAt < staleBefore))
            .OrderBy(r => r.Id).Take(limit)
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (claimIds.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await db.SearchRequests
            .Where(r => claimIds.Contains(r.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProcessingStatus, 1)
                .SetProperty(r => r.ClaimToken, token)
                .SetProperty(r => r.ClaimedAt, now)
                .SetProperty(r => r.LastError, (string?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);

        var sequence = sequenceStart;
        var maxSequence = await db.SearchBatches
            .Where(b => b.BusinessDate == businessDate)
            .MaxAsync(b => (int?)b.FileSequence, ct);
        if (maxSequence is not null) sequence = Math.Max(sequenceStart, maxSequence.Value + 1);

        var records = await ReadClaimAsync(db, token, ct);
        db.SearchBatches.Add(new SearchBatchEntity
        {
            BusinessDate = businessDate,
            FileSequence = sequence,
            ClaimToken = token,
            RecordCount = records.Count,
            Status = 1,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new SearchClaim(token, sequence, records);
    }

    public async Task CompleteAsync(SearchClaim claim, string fileName, string filePath, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var lineById = claim.Records.Select((record, index) => (record.Id, Line: index + 1))
            .ToDictionary(item => item.Id, item => item.Line);
        var claimIds = lineById.Keys.ToList();
        var requests = await db.SearchRequests
            .Where(r => claimIds.Contains(r.Id) && r.ClaimToken == claim.Token && r.ProcessingStatus == 1)
            .ToListAsync(ct);
        foreach (var request in requests)
        {
            request.ProcessingStatus = 2;
            request.ProcessedAt = now;
            request.OutputFileName = fileName;
            request.OutputLineNumber = lineById[request.Id];
            request.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await db.SearchBatches
            .Where(b => b.ClaimToken == claim.Token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, 2)
                .SetProperty(b => b.FileName, fileName)
                .SetProperty(b => b.FilePath, filePath)
                .SetProperty(b => b.CompletedAt, now), ct);
        await tx.CommitAsync(ct);
    }

    public async Task FailAsync(SearchClaim claim, string failureMessage, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        await db.SearchRequests
            .Where(r => r.ClaimToken == claim.Token && r.ProcessingStatus == 1)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProcessingStatus, 3)
                .SetProperty(r => r.LastError, failureMessage)
                .SetProperty(r => r.UpdatedAt, now), ct);
        await db.SearchBatches
            .Where(b => b.ClaimToken == claim.Token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, 3)
                .SetProperty(b => b.Error, failureMessage)
                .SetProperty(b => b.CompletedAt, now), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<SearchGeneratedBatch?> GetGeneratedBatchAsync(string? fileName, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var query = db.SearchBatches.AsNoTracking().Where(b => b.Status == 2);
        if (fileName is not null) query = query.Where(b => b.FileName == fileName);
        var batch = await query.OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
        if (batch is null) return null;
        if (string.IsNullOrWhiteSpace(batch.FilePath) || string.IsNullOrWhiteSpace(batch.FileName)) return null;
        return new SearchGeneratedBatch(batch.Id, batch.FileName, batch.FilePath, batch.RecordCount ?? 0);
    }

    public async Task RecordFvuAsync(long batchId, bool passed, string? zipPath, string? hash, string? failureMessage, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await db.SearchBatches
            .Where(b => b.Id == batchId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, passed ? 4 : 5)
                .SetProperty(b => b.FvuZipPath, zipPath)
                .SetProperty(b => b.FvuHash, hash)
                .SetProperty(b => b.Error, failureMessage)
                .SetProperty(b => b.CompletedAt, DateTime.UtcNow), ct);
    }

    public async Task<SearchResponseImportResult> ImportResponseAsync(SearchResponseImport response, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync($"CKYC:search-response:{response.SourceHash}", ct);

        var duplicate = await db.SearchResponseFiles.AnyAsync(f => f.SourceHash == response.SourceHash, ct);
        if (duplicate)
        {
            await tx.RollbackAsync(ct);
            return new SearchResponseImportResult(0, 0, true);
        }

        var searchBatchId = await db.SearchBatches
            .Where(b => b.FileName == response.InputFileName)
            .OrderByDescending(b => b.Id)
            .Select(b => (long?)b.Id)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        db.SearchResponseFiles.Add(new SearchResponseFileEntity
        {
            SearchBatchId = searchBatchId,
            ResponseFileName = response.Header.ResponseFileName,
            ResponseFileNumber = response.Header.ResponseFileNumber,
            FiCode = response.Header.FiCode,
            RegionCode = response.Header.RegionCode,
            TotalRecords = response.Header.TotalRecords,
            TotalProcessed = response.Header.TotalProcessed,
            RecordsUnderProcessing = response.Header.RecordsUnderProcessing,
            RecordsFailed = response.Header.RecordsFailed,
            ResponseTimestamp = response.Header.ResponseTimestamp,
            Filler = response.Header.Filler,
            RawHeaderData = response.Header.RawHeaderData,
            SourceArchiveName = response.SourceArchiveName,
            SourceHash = response.SourceHash,
            CreatedAt = now,
        });
        var responseLines = response.Details
            .Where(d => d.InputRecordLineNumber is not null)
            .Select(d => d.InputRecordLineNumber!.Value).Distinct().ToList();
        var matchedRequests = await db.SearchRequests
            .Where(r => r.OutputFileName == response.InputFileName
                     && r.OutputLineNumber != null && responseLines.Contains(r.OutputLineNumber.Value))
            .ToListAsync(ct);
        var requestByLine = matchedRequests.GroupBy(r => r.OutputLineNumber!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(r => r.Id).First());

        var matched = 0;
        foreach (var detail in response.Details)
        {
            var request = detail.InputRecordLineNumber is not null
                && requestByLine.TryGetValue(detail.InputRecordLineNumber.Value, out var found) ? found : null;
            long? requestId = request?.Id;

            db.SearchResponses.Add(new SearchResponseEntity
            {
                SearchRequestId = requestId,
                ResponseFileName = response.Header.ResponseFileName,
                ResponseFileNumber = response.Header.ResponseFileNumber,
                LineNumber = detail.LineNumber,
                InputRecordLineNumber = detail.InputRecordLineNumber,
                ClientType = detail.ClientType,
                SearchByOvdType = detail.SearchByOvdType,
                SearchByOvdNumber = detail.SearchByOvdNumber,
                SearchKey = detail.SearchKey,
                CkycReferenceNumber = detail.CkycReferenceNumber,
                FirstName = detail.FirstName,
                MiddleName = detail.MiddleName,
                LastName = detail.LastName,
                Gender = detail.Gender,
                MobileNumber = detail.MobileNumber,
                EmailAddress = detail.EmailAddress,
                LastUpdatedDate = detail.LastUpdatedDate,
                Cin = detail.Cin,
                LegalEntityName = detail.LegalEntityName,
                PhotoReference = detail.PhotoReference,
                RegistrationDate = detail.RegistrationDate,
                DeactivationReason = detail.DeactivationReason,
                Remark = detail.Remark,
                PanDocument = At(detail.DocumentFlags, 0),
                AadhaarDocument = At(detail.DocumentFlags, 1),
                PassportDocument = At(detail.DocumentFlags, 2),
                DrivingLicenseDocument = At(detail.DocumentFlags, 3),
                VoterIdDocument = At(detail.DocumentFlags, 4),
                NregaDocument = At(detail.DocumentFlags, 5),
                DisabilityDocument = At(detail.DocumentFlags, 6),
                Form6061Document = At(detail.DocumentFlags, 7),
                ForeignJurisdictionDocument = At(detail.DocumentFlags, 8),
                NprDocument = At(detail.DocumentFlags, 9),
                UtilityBillDocument = At(detail.DocumentFlags, 10),
                IncorporationDocument = At(detail.DocumentFlags, 11),
                MemorandumDocument = At(detail.DocumentFlags, 12),
                RegistrationCertificate = At(detail.DocumentFlags, 13),
                PartnershipDeed = At(detail.DocumentFlags, 14),
                TrustDeed = At(detail.DocumentFlags, 15),
                SupportingPoiDocument = At(detail.DocumentFlags, 16),
                OtherDocument = At(detail.DocumentFlags, 17),
                Filler1 = At(detail.Fillers, 0),
                Filler2 = At(detail.Fillers, 1),
                Filler3 = At(detail.Fillers, 2),
                Filler4 = At(detail.Fillers, 3),
                Filler5 = At(detail.Fillers, 4),
                Filler6 = At(detail.Fillers, 5),
                Filler7 = At(detail.Fillers, 6),
                Filler8 = At(detail.Fillers, 7),
                RecordLevelHash = detail.RecordLevelHash,
                RawResponseData = detail.RawResponseData,
                CreatedAt = now,
            });
            if (request is null) continue;
            matched++;
            request.ResponseStatus = "ResponseRead";
            request.LastSearchKey = detail.SearchKey;
            request.LastCkycReference = detail.CkycReferenceNumber;
            request.LastResponseRemark = detail.Remark;
            request.ResponseReadAt = now;
            request.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new SearchResponseImportResult(response.Details.Count, matched, false);
    }

    private static string? At(string?[] values, int index) => index < values.Length ? values[index] : null;

    private static async Task<List<SearchRequest>> ReadClaimAsync(CkycDbContext db, string token, CancellationToken ct)
    {
        var rows = await db.SearchRequests.AsNoTracking()
            .Where(r => r.ClaimToken == token)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        return rows.Select(r => new SearchRequest
        {
            Id = r.Id,
            ExternalRequestId = r.ExternalRequestId,
            CustomerId = r.CustomerId,
            ClientType = r.ClientType ?? "I",
            SearchOption = r.SearchOption ?? 0,
            IdentityTypeAndNumber = r.IdentityTypeAndNumber,
            FirstName = r.FirstName,
            MiddleName = r.MiddleName,
            LastName = r.LastName,
            DateOfBirth = r.DateOfBirth,
            LegalEntityName = r.LegalEntityName,
            DateOfIncorporation = r.DateOfIncorporation,
            Gender = r.Gender,
            PhotoReferenceNumber = r.PhotoReferenceNumber,
            Relation = r.Relation,
            RelationFirstName = r.RelationFirstName,
            RelationMiddleName = r.RelationMiddleName,
            RelationLastName = r.RelationLastName,
            MobileNumber = r.MobileNumber,
            VerifiableCredential = r.VerifiableCredential,
            Constitution = r.Constitution,
            RawRequestJson = r.RawRequestJson,
            ProcessingStatus = r.ProcessingStatus ?? 0,
            ClaimToken = r.ClaimToken,
            ClaimedAt = r.ClaimedAt,
        }).ToList();
    }
}
