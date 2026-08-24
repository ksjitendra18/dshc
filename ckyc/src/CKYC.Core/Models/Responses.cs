namespace CKYC.Core.Models;

/// <summary>Parsed record-90 header of a CERSAI response (<c>*.UPL.RESm</c>) file.</summary>
public sealed record ResponseHeader(
    int TotalRecords,
    int TotalProcessed,
    int UnderProcessing,
    int Failed,
    string? ResponseTimestamp);

/// <summary>Parsed record-100 detail of a CERSAI response file (one per submitted record).</summary>
public sealed record ResponseDetail(
    int LineNumber,
    int? InputRecordLineNumber,
    string? AckNumber,
    string? RecordStatus,
    string? CkycReferenceNumber,
    string? CkycNumber,
    string? RejectionRemark);

/// <summary>A fully-parsed CERSAI response file.</summary>
public sealed record CkycResponseFile(
    string FileName,
    int ResponseFileNumber,
    ResponseHeader? Header,
    IReadOnlyList<ResponseDetail> Details);
