namespace CKYC.Core.Domain;

public sealed class DownloadResponseImport
{
    public string ResponseFileName { get; set; } = string.Empty;
    public int ResponseFileNumber { get; set; }
    public string? FiCode { get; set; }
    public string? RegionCode { get; set; }
    public string? ClientType { get; set; }
    public int? TotalRecords { get; set; }
    public string? Version { get; set; }
    public string? ResponseDate { get; set; }
    public string RawHeaderData { get; set; } = string.Empty;
    public string SourceArchiveName { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public List<DownloadResponseLine> Lines { get; set; } = [];
    public List<DownloadArtifact> Artifacts { get; set; } = [];
}

public sealed record DownloadResponseLine(
    string SourceEntryPath, string RecordType, int? LineNumber, int? InputRecord20LineNumber, string? CkycNumber, string RawData);

public sealed record DownloadArtifact(string EntryPath, string FileName, long Size, string Sha256);

public sealed record DownloadImportResult(int Lines, int Artifacts, bool AlreadyImported);
