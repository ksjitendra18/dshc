namespace CKYC.Core.Domain;

/// <summary>
/// Lifecycle status of a single source customer as it flows through the CKYC pipeline.
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
}

public static class MasterRecordStatusExtensions
{
    public static bool IsTerminal(this MasterRecordStatus status) =>
        status is MasterRecordStatus.FvuPassed
            or MasterRecordStatus.Failed
            or MasterRecordStatus.Reconciled
            or MasterRecordStatus.Rejected;

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
        _ => status.ToString(),
    };
}
