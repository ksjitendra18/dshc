namespace CKYC.Core.Domain;

public sealed class SearchResponseHeader
{
    public string ResponseFileName { get; set; } = "";
    public int ResponseFileNumber { get; set; }
    public string? FiCode { get; set; }
    public string? RegionCode { get; set; }
    public int? TotalRecords { get; set; }
    public int? TotalProcessed { get; set; }
    public int? RecordsUnderProcessing { get; set; }
    public int? RecordsFailed { get; set; }
    public string? ResponseTimestamp { get; set; }
    public string? Filler { get; set; }
    public string RawHeaderData { get; set; } = "";
}

public sealed class SearchResponseDetail
{
    public int? LineNumber { get; set; }
    public string? ClientType { get; set; }
    public int? InputRecordLineNumber { get; set; }
    public string? SearchByOvdType { get; set; }
    public string? SearchByOvdNumber { get; set; }
    public string? SearchKey { get; set; }
    public string? CkycReferenceNumber { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Gender { get; set; }
    public string? MobileNumber { get; set; }
    public string? EmailAddress { get; set; }
    public string? LastUpdatedDate { get; set; }
    public string? Cin { get; set; }
    public string? LegalEntityName { get; set; }
    public string? PhotoReference { get; set; }
    public string? RegistrationDate { get; set; }
    public string? DeactivationReason { get; set; }
    public string? Remark { get; set; }
    public string?[] DocumentFlags { get; set; } = new string?[18];
    public string?[] Fillers { get; set; } = new string?[8];
    public string? RecordLevelHash { get; set; }
    public string RawResponseData { get; set; } = "";
}

public sealed record SearchResponseImport(
    string SourceArchiveName,
    string SourceHash,
    string InputFileName,
    SearchResponseHeader Header,
    IReadOnlyList<SearchResponseDetail> Details);

public sealed record SearchResponseImportResult(int Inserted, int MatchedRequests, bool AlreadyImported);
