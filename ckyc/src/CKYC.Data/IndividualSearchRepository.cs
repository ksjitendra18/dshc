using CKYC.Core.Abstractions;
using CKYC.Core.Domain;
using Microsoft.EntityFrameworkCore;
using IndividualSearchEntity = CKYC.Data.Entities.IndividualSearch;

namespace CKYC.Data;

/// <summary>
/// EF Core (SQL Server) persistence for the pre-batch customer search rows
/// (<c>individual_search</c>). One customer can have many rows — a candidate per identity
/// document held plus a name-level fallback — each carrying its own API result.
/// </summary>
public sealed class IndividualSearchRepository : IIndividualSearchRepository
{
    private readonly ICkycDatabase _db;

    public IndividualSearchRepository(ICkycDatabase db) => _db = db;

    public async Task<int> InsertAsync(IReadOnlyList<IndividualSearch> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return 0;
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireTransactionLockAsync($"CKYC:individual-search:{rows[0].MasterRecordId}", ct);

        // Idempotent per master record: never build the same candidate set twice.
        var masterRecordId = rows[0].MasterRecordId;
        await db.IndividualSearches
            .Where(r => r.MasterRecordId == masterRecordId)
            .ExecuteDeleteAsync(ct);

        foreach (var row in rows)
        {
            db.IndividualSearches.Add(new IndividualSearchEntity
            {
                MasterRecordId = row.MasterRecordId,
                CustomerId = row.CustomerId,
                ClientType = row.ClientType,
                SearchOption = row.SearchOption,
                IdentityTypeAndNumber = row.IdentityTypeAndNumber,
                FirstName = row.FirstName,
                MiddleName = row.MiddleName,
                LastName = row.LastName,
                DateOfBirth = row.DateOfBirth,
                LegalEntityName = row.LegalEntityName,
                DateOfIncorporation = row.DateOfIncorporation,
                Gender = row.Gender,
                PhotoReferenceNumber = row.PhotoReferenceNumber,
                Relation = row.Relation,
                RelationFirstName = row.RelationFirstName,
                RelationMiddleName = row.RelationMiddleName,
                RelationLastName = row.RelationLastName,
                MobileNumber = row.MobileNumber,
                VerifiableCredential = row.VerifiableCredential,
                Constitution = row.Constitution,
                RawRequestJson = row.RawRequestJson,
                ProcessingStatus = (int)IndividualSearchProcessingStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Count;
    }

    public async Task<IReadOnlyList<IndividualSearch>> GetByMasterRecordAsync(long masterRecordId, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        var rows = await db.IndividualSearches.AsNoTracking()
            .Where(r => r.MasterRecordId == masterRecordId)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<bool> ExistsForMasterRecordAsync(long masterRecordId, CancellationToken ct = default)
    {
        await using var db = _db.CreateContext();
        return await db.IndividualSearches.AnyAsync(r => r.MasterRecordId == masterRecordId, ct);
    }

    public async Task CompleteAsync(IndividualSearch row, IndividualSearchApiResult result, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        await db.IndividualSearches
            .Where(r => r.Id == row.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProcessingStatus, (int)IndividualSearchProcessingStatus.Completed)
                .SetProperty(r => r.ProcessedAt, now)
                .SetProperty(r => r.Outcome, IndividualSearchOutcomeCodes.For(result.Outcome))
                .SetProperty(r => r.SearchKey, result.SearchKey)
                .SetProperty(r => r.CkycReferenceNumber, result.CkycReferenceNumber)
                .SetProperty(r => r.ResponseRemark, result.Remark)
                .SetProperty(r => r.RawResponseJson, result.RawResponseJson)
                .SetProperty(r => r.ResponseReadAt, now)
                .SetProperty(r => r.LastError, result.Error)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }

    public async Task FailAsync(IndividualSearch row, string failureMessage, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        await db.IndividualSearches
            .Where(r => r.Id == row.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProcessingStatus, (int)IndividualSearchProcessingStatus.Failed)
                .SetProperty(r => r.Outcome, IndividualSearchOutcomeCodes.Error)
                .SetProperty(r => r.ProcessedAt, now)
                .SetProperty(r => r.LastError, failureMessage)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }

    public async Task SkipAsync(IndividualSearch row, string reason, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var db = _db.CreateContext();
        await db.IndividualSearches
            .Where(r => r.Id == row.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProcessingStatus, (int)IndividualSearchProcessingStatus.Completed)
                .SetProperty(r => r.ProcessedAt, now)
                .SetProperty(r => r.Outcome, IndividualSearchOutcomeCodes.Skipped)
                .SetProperty(r => r.ResponseRemark, reason)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }

    private static IndividualSearch ToDomain(IndividualSearchEntity r) => new()
    {
        Id = r.Id,
        MasterRecordId = r.MasterRecordId ?? 0,
        CustomerId = r.CustomerId ?? string.Empty,
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
        ProcessingStatus = (IndividualSearchProcessingStatus)(r.ProcessingStatus ?? 0),
        ClaimToken = r.ClaimToken,
        ClaimedAt = r.ClaimedAt,
        ProcessedAt = r.ProcessedAt,
        Outcome = r.Outcome is null ? null : IndividualSearchOutcomeCodes.Parse(r.Outcome),
        SearchKey = r.SearchKey,
        CkycReferenceNumber = r.CkycReferenceNumber,
        ResponseRemark = r.ResponseRemark,
        ResponseReadAt = r.ResponseReadAt,
        RawResponseJson = r.RawResponseJson,
        LastError = r.LastError,
        CreatedAt = r.CreatedAt ?? DateTime.MinValue,
        UpdatedAt = r.UpdatedAt ?? DateTime.MinValue,
    };
}
