namespace CKYC.Core.Domain;

/// <summary>
/// Lifecycle status of a single customer as it flows through the CKYC pipeline.
/// Persisted in the master table's Status column — this is the single "current stage"
/// value that tells you where a record is right now (awaiting batch, uploaded & pending
/// at CERSAI, response read, reconciled, etc.).
///
/// Numeric values are append-only so existing databases are never reinterpreted:
/// 0–6 are the original stages, 7+ were added when response/reconciliation tracking was
/// introduced. Do NOT renumber existing members.
/// </summary>
public enum MasterRecordStatus
{
    /// <summary>Newly fetched from the source — awaiting CRM enrichment.</summary>
    Pending = 0,

    /// <summary>CRM data was fetched successfully for this customer.</summary>
    CrmFetched = 1,

    /// <summary>Individual details saved to the record tables.</summary>
    Saved = 2,

    /// <summary>Record was enqueued into the generated batch (.UPL) file — awaiting upload.</summary>
    Batched = 3,

    /// <summary>Batch was submitted to the FVU and passed validation.</summary>
    FvuPassed = 4,

    /// <summary>Batch was submitted to the FVU and failed validation.</summary>
    FvuFailed = 5,

    /// <summary>A permanent failure occurred (e.g. record could not be saved after retries).</summary>
    Failed = 6,

    /// <summary>Batch uploaded / submitted to CERSAI — record is pending a response.</summary>
    Uploaded = 7,

    /// <summary>At least one CERSAI response file has been read for this record.</summary>
    ResponseRead = 8,

    /// <summary>Record reconciled (matched/resolved against the CERSAI reply).</summary>
    Reconciled = 9,

    /// <summary>Record permanently rejected by CERSAI.</summary>
    Rejected = 10,

    /// <summary>
    /// Daily customer-id fetch from the CBS failed. Available for operator reports;
    /// the pipeline itself still treats a failed fetch as retryable Pending.
    /// </summary>
    DataFetchFailed = 11,

    /// <summary>
    /// Individual details are saved and the record is awaiting the pre-batch customer
    /// search (the API check that decides whether the customer already has a CKYC record).
    /// </summary>
    PendingSearch = 12,

    /// <summary>
    /// Customer search completed without a match; the CKYC search key returned by the API
    /// has been written to record 20 and the record is ready to batch.
    /// </summary>
    Searched = 13,

    /// <summary>
    /// Customer search found an existing CKYC record (CKYC reference number); the record
    /// already exists and is not pushed through CKYC creation again.
    /// </summary>
    SearchFound = 14,

    /// <summary>
    /// Individual details are saved and the record is awaiting its supporting image/document
    /// fetch from the intake channel's source (e.g. the beckyc SFTP folder). Records in this
    /// state are deliberately <b>not</b> batched — the image step must complete first.
    /// </summary>
    ImagePending = 15,

    /// <summary>
    /// The supporting image/document could not be fetched (missing in the source). The record
    /// is blocked from batching and is retryable through the <c>ImageFetch</c> activity.
    /// </summary>
    ImageFailed = 16,
}

/// <summary>
/// The compact 2–3 character code persisted in <c>master_record.StatusCode</c> (kept in
/// sync with the numeric <see cref="MasterRecordStatus"/> on every status write) and
/// seeded in <c>status_master</c> (append-only — never renumber or reuse).
/// </summary>
public static class MasterRecordStatusCode
{
    public const string Pending = "PND";
    public const string CrmFetched = "CRM";
    public const string Saved = "SAV";
    public const string Batched = "BAT";
    public const string FvuPassed = "FVP";
    public const string FvuFailed = "FVF";
    public const string Failed = "FLD";
    public const string Uploaded = "UPL";
    public const string ResponseRead = "RSP";
    public const string Reconciled = "RCN";
    public const string Rejected = "REJ";
    public const string DataFetchFailed = "DTF";
    public const string PendingSearch = "SRP";
    public const string Searched = "SRD";
    public const string SearchFound = "SRF";
    public const string ImagePending = "IMP";
    public const string ImageFailed = "IMF";

    public static string For(MasterRecordStatus status) => status switch
    {
        MasterRecordStatus.Pending => Pending,
        MasterRecordStatus.CrmFetched => CrmFetched,
        MasterRecordStatus.Saved => Saved,
        MasterRecordStatus.Batched => Batched,
        MasterRecordStatus.FvuPassed => FvuPassed,
        MasterRecordStatus.FvuFailed => FvuFailed,
        MasterRecordStatus.Failed => Failed,
        MasterRecordStatus.Uploaded => Uploaded,
        MasterRecordStatus.ResponseRead => ResponseRead,
        MasterRecordStatus.Reconciled => Reconciled,
        MasterRecordStatus.Rejected => Rejected,
        MasterRecordStatus.DataFetchFailed => DataFetchFailed,
        MasterRecordStatus.PendingSearch => PendingSearch,
        MasterRecordStatus.Searched => Searched,
        MasterRecordStatus.SearchFound => SearchFound,
        MasterRecordStatus.ImagePending => ImagePending,
        MasterRecordStatus.ImageFailed => ImageFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status."),
    };
}

public static class MasterRecordStatusExtensions
{
    public static bool IsTerminal(this MasterRecordStatus status) =>
        status is MasterRecordStatus.FvuPassed
            or MasterRecordStatus.Failed
            or MasterRecordStatus.Reconciled
            or MasterRecordStatus.Rejected
            or MasterRecordStatus.SearchFound;

    /// <summary>Short human label for reporting (e.g. the `status` command).</summary>
    public static string Label(this MasterRecordStatus status) => status switch
    {
        MasterRecordStatus.Pending => "Pending (awaiting CRM)",
        MasterRecordStatus.CrmFetched => "CRM fetched",
        MasterRecordStatus.Saved => "Saved (awaiting batch)",
        MasterRecordStatus.Batched => "Batched (awaiting upload)",
        MasterRecordStatus.FvuPassed => "FVU passed",
        MasterRecordStatus.FvuFailed => "FVU failed",
        MasterRecordStatus.Failed => "Failed",
        MasterRecordStatus.Uploaded => "Uploaded (pending at CERSAI)",
        MasterRecordStatus.ResponseRead => "Response read",
        MasterRecordStatus.Reconciled => "Reconciled",
        MasterRecordStatus.Rejected => "Rejected",
        MasterRecordStatus.DataFetchFailed => "Data fetch failed",
        MasterRecordStatus.PendingSearch => "Pending search",
        MasterRecordStatus.Searched => "Searched (awaiting batch)",
        MasterRecordStatus.SearchFound => "Search found (existing CKYC)",
        MasterRecordStatus.ImagePending => "Image pending (awaiting document fetch)",
        MasterRecordStatus.ImageFailed => "Image fetch failed (blocked from batch)",
        _ => status.ToString(),
    };
}
