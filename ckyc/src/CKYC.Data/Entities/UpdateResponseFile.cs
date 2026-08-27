using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class UpdateResponseFile
{
    public long Id { get; set; }

    public long? UpdateBatchId { get; set; }

    public string? ResponseFileName { get; set; }

    public int? ResponseFileNumber { get; set; }

    public string? ClientType { get; set; }

    public string? FiCode { get; set; }

    public string? RegionCode { get; set; }

    public int? TotalRecords { get; set; }

    public int? TotalProcessed { get; set; }

    public int? RecordsUnderProcessing { get; set; }

    public int? RecordsFailed { get; set; }

    public string? ResponseTimestamp { get; set; }

    public string? Filler1 { get; set; }

    public string? Filler2 { get; set; }

    public string? RawHeaderData { get; set; }

    public string? SourceArchiveName { get; set; }

    public string? SourceHash { get; set; }

    public DateTime? CreatedAt { get; set; }
}
