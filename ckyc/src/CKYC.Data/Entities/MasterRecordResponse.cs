using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class MasterRecordResponse
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public string? BatchFile { get; set; }

    public int? ResponseFileNumber { get; set; }

    public string? ResponseFileName { get; set; }

    public int? LineNumber { get; set; }

    public int? InputRecordLineNumber { get; set; }

    public string? AckNumber { get; set; }

    public string? RecordStatus { get; set; }

    public string? CkycReferenceNumber { get; set; }

    public string? CkycNumber { get; set; }

    public string? RejectionRemark { get; set; }

    public DateTime? ReadAt { get; set; }

    public string? Remarks { get; set; }

    public string? RawData { get; set; }

    public DateTime? CreatedAt { get; set; }
}
