using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class DownloadResponseArtifact
{
    public long Id { get; set; }

    public long? DownloadResponseFileId { get; set; }

    public string? EntryPath { get; set; }

    public string? FileName { get; set; }

    public long? Size { get; set; }

    public string? Sha256 { get; set; }

    public DateTime? CreatedAt { get; set; }
}
