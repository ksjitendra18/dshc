using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class UploadResponseFile
{
    public long Id { get; set; }

    public string? BatchFile { get; set; }

    public string? ResponseFileName { get; set; }

    public int? ResponseFileNumber { get; set; }

    public int? TotalRecords { get; set; }

    public int? TotalProcessed { get; set; }

    public int? UnderProcessing { get; set; }

    public int? Failed { get; set; }

    public string? ResponseTimestamp { get; set; }

    public string? RawHeaderData { get; set; }

    public string? SourceArchiveName { get; set; }

    public string? SourceHash { get; set; }

    public DateTime? CreatedAt { get; set; }
}
