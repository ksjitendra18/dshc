using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class Batch
{
    public long Id { get; set; }

    public string? BatchKey { get; set; }

    public string? UploadFileName { get; set; }

    public string? UploadFilePath { get; set; }

    public string? ZipPath { get; set; }

    public int? RecordCount { get; set; }

    public DateTime? CreatedAt { get; set; }
}
