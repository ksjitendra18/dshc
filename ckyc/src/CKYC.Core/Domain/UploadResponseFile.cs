namespace CKYC.Core.Domain;

/// <summary>Audit/header row for one received CERSAI upload response file.</summary>
public sealed class UploadResponseFile
{
    public string BatchFile { get; set; } = string.Empty;
    public string ResponseFileName { get; set; } = string.Empty;
    public int ResponseFileNumber { get; set; }
    public int TotalRecords { get; set; }
    public int TotalProcessed { get; set; }
    public int UnderProcessing { get; set; }
    public int Failed { get; set; }
    public string? ResponseTimestamp { get; set; }
    public string? RawHeaderData { get; set; }
    public string SourceArchiveName { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
}
