using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class DownloadResponseLine
{
    public long Id { get; set; }

    public long? DownloadResponseFileId { get; set; }

    public string? SourceEntryPath { get; set; }

    public string? RecordType { get; set; }

    public int? LineNumber { get; set; }

    public int? InputRecord20LineNumber { get; set; }

    public string? CkycNumber { get; set; }

    public string? RawData { get; set; }

    public DateTime? CreatedAt { get; set; }
}
