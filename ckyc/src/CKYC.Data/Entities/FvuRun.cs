using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class FvuRun
{
    public long Id { get; set; }

    public string? BatchKey { get; set; }

    public int? Executed { get; set; }

    public int? ExitCode { get; set; }

    public int? Passed { get; set; }

    public string? SummaryJson { get; set; }

    public string? OutputZipPath { get; set; }

    public string? HashValue { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? CreatedAt { get; set; }
}
