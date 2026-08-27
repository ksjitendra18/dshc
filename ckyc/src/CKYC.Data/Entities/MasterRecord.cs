using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class MasterRecord
{
    public long Id { get; set; }

    public string? CustomerId { get; set; }

    public string? ClientType { get; set; }

    public DateOnly? BusinessDate { get; set; }

    public int? Status { get; set; }

    public string? StatusCode { get; set; }

    public string? Remarks { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public string? LastActivity { get; set; }

    public DateTime? NextRetryAt { get; set; }

    public int? NeedsReconcile { get; set; }

    public int? ReattemptCount { get; set; }

    public DateTime? ReattemptedAt { get; set; }

    public string? BatchFile { get; set; }

    public int? BatchRecordLine { get; set; }

    public int? IsCrmFetched { get; set; }

    public int? IsSaved { get; set; }

    public int? IsBatched { get; set; }

    public int? IsUploaded { get; set; }

    public int? IsResponseRead { get; set; }

    public int? IsReconciled { get; set; }

    public int? IsRejected { get; set; }

    public DateTime? CrmFetchedAt { get; set; }

    public DateTime? SavedAt { get; set; }

    public DateTime? BatchedAt { get; set; }

    public DateTime? UploadedAt { get; set; }

    public DateTime? FirstResponseAt { get; set; }

    public DateTime? ReconciledAt { get; set; }

    public int? LastResponseFileNumber { get; set; }

    public string? LastResponseFileName { get; set; }

    public string? LastResponseAckNumber { get; set; }

    public string? LastResponseStatus { get; set; }

    public string? LastResponseCkycReference { get; set; }

    public string? LastResponseCkycNumber { get; set; }

    public string? LastResponseRejectionRemark { get; set; }

    public DateTime? LastResponseReadAt { get; set; }

    public string? LastResponseRemarks { get; set; }

    public string? ReconStatus { get; set; }

    public string? ReconRemarks { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<IndividualDocument> IndividualDocuments { get; set; } = new List<IndividualDocument>();

    public virtual ICollection<LegalEntityDocument> LegalEntityDocuments { get; set; } = new List<LegalEntityDocument>();
}
