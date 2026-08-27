namespace CKYC.Core.Domain;

/// <summary>
/// One bulk-update submission (one CKYC record being amended). Mirrors the intake row of
/// the "Structure for Bulk Update file of KYC Records" workbooks
/// (<c>vendor/individual-format-update.xlsx</c> and <c>vendor/legal-format-update.xlsx</c>):
/// every detail line in a .UPD file begins with Record Type, Line Number and the existing
/// 14-digit CKYC Number, followed by per-section <c>*Update Flg</c> fields plus the values
/// being changed. Sections that are not flagged stay blank.
///
/// Field values are held in a flat case-insensitive dictionary keyed by the stable camel-case
/// keys declared in <see cref="Spec.UpdateFormat"/> (e.g. <c>firstName</c>,
/// <c>permPinCode</c>, <c>entityConstitution</c>). This keeps the loader generic while the
/// writer stays byte-exact with the vendor field order.
/// </summary>
public sealed class UpdateRequest
{
    public long Id { get; set; }
    public string? ExternalRequestId { get; set; }
    public string? CustomerId { get; set; }

    /// <summary>I-Individual, L-Legal Entity (drives which format catalog + writer applies).</summary>
    public string ClientType { get; set; } = "I";

    /// <summary>The existing CKYC number of the record being updated (record 20, mandatory).</summary>
    public string CkycNumber { get; set; } = string.Empty;

    /// <summary>Submitted field values keyed by <see cref="Spec.UpdateFormat"/> field keys.</summary>
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Original JSON body, re-parsed verbatim when a claimed record is processed.</summary>
    public string? RawRequestJson { get; set; }

    // ---- processing state (search_request conventions) ----
    public int ProcessingStatus { get; set; }   // 0 pending, 1 claimed, 2 processed, 3 failed
    public string? ClaimToken { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? OutputFileName { get; set; }
    public int OutputLineNumber { get; set; }   // the "20" line number inside OutputFileName
    public string? OutputBatchKey { get; set; }
    public string? ResponseStatus { get; set; } // No Match / Rejected (record-90 status 02/03)
    public string? LastAckNumber { get; set; }
    public string? LastResponseStatusCode { get; set; }
    public string? LastResponseRemark { get; set; }
    public DateTime? ResponseReadAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>An atomically claimed group of pending update requests for one client type.</summary>
public sealed record UpdateClaim(
    string Token, DateOnly BusinessDate, int FileSequence, string ClientType,
    IReadOnlyList<UpdateRequest> Records);

/// <summary>A generated-but-not-yet-FVU-validated .UPD file.</summary>
public sealed record UpdateGeneratedBatch(long Id, string FileName, string FilePath, int RecordCount);

public readonly record struct UpdateIngestResult(int Inserted, int Total);

/// <summary>A parsed .UPD.RESm header (record type 80).</summary>
public sealed class UpdateResponseHeader
{
    public string ResponseFileName { get; set; } = string.Empty;
    public int ResponseFileNumber { get; set; }
    public string? ClientType { get; set; }
    public string? FiCode { get; set; }
    public string? RegionCode { get; set; }
    public int? TotalRecords { get; set; }
    public int? TotalProcessed { get; set; }
    public int? RecordsUnderProcessing { get; set; }
    public int? RecordsFailed { get; set; }
    public string? ResponseTimestamp { get; set; }
    public string? Filler1 { get; set; }
    public string? Filler2 { get; set; }
    public string RawHeaderData { get; set; } = string.Empty;
}

/// <summary>A parsed .UPD.RESm detail line (record type 90).</summary>
public sealed class UpdateResponseDetail
{
    public int? LineNumber { get; set; }
    /// <summary>"Line Number of record type 20" — points at the submitted customer's record-20 line.</summary>
    public int? InputRecord20LineNumber { get; set; }
    public string? AckNumber { get; set; }

    /// <summary>02 = No Match, 03 = Rejected (per the Update_response sheets).</summary>
    public string? RecordStatus { get; set; }

    /// <summary>Returned when the record status is 02.</summary>
    public string? CkycNumber { get; set; }

    /// <summary>Applicable only when the record status is 03.</summary>
    public string? RejectionRemark { get; set; }

    public string RawResponseData { get; set; } = string.Empty;
}

public sealed record UpdateResponseImport(
    string SourceArchiveName, string SourceHash, string InputFileName,
    UpdateResponseHeader Header, IReadOnlyList<UpdateResponseDetail> Details);

public sealed record UpdateResponseImportResult(int Inserted, int MatchedRequests, bool AlreadyImported);
