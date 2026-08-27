using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class SearchBatch
{
    public long Id { get; set; }

    public DateOnly? BusinessDate { get; set; }

    public int? FileSequence { get; set; }

    public string? ClaimToken { get; set; }

    public int? RecordCount { get; set; }

    public int? Status { get; set; }

    public string? FileName { get; set; }

    public string? FilePath { get; set; }

    public string? FvuZipPath { get; set; }

    public string? FvuHash { get; set; }

    public string? Error { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
