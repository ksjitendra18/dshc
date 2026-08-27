using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class DownloadResponseFile
{
    public long Id { get; set; }

    public string? ResponseFileName { get; set; }

    public int? ResponseFileNumber { get; set; }

    public string? FiCode { get; set; }

    public string? RegionCode { get; set; }

    public string? ClientType { get; set; }

    public int? TotalRecords { get; set; }

    public string? Version { get; set; }

    public string? ResponseDate { get; set; }

    public string? RawHeaderData { get; set; }

    public string? SourceArchiveName { get; set; }

    public string? SourceHash { get; set; }

    public DateTime? CreatedAt { get; set; }
}
