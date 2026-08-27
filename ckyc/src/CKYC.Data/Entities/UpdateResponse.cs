using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class UpdateResponse
{
    public long Id { get; set; }

    public long? UpdateRequestId { get; set; }

    public string? ResponseFileName { get; set; }

    public int? ResponseFileNumber { get; set; }

    public int? LineNumber { get; set; }

    public int? InputRecord20LineNumber { get; set; }

    public string? AckNumber { get; set; }

    public string? RecordStatus { get; set; }

    public string? CkycNumber { get; set; }

    public string? RejectionRemark { get; set; }

    public string? RawResponseData { get; set; }

    public DateTime? CreatedAt { get; set; }
}
