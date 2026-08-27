using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class MasterRecordBatch
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public string? BatchFile { get; set; }

    public int? Record20LineNumber { get; set; }

    public DateTime? BatchedAt { get; set; }
}
