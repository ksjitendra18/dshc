using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class LegalEntityDocument
{
    public long Id { get; set; }

    public long MasterRecordId { get; set; }

    public long FileContentId { get; set; }

    public string OriginalFileName { get; set; } = null!;

    public string CanonicalFileName { get; set; } = null!;

    public string MediaType { get; set; } = null!;

    public string? DocumentKind { get; set; }

    public string SourceType { get; set; } = null!;

    public string? SourceReference { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual FileContent FileContent { get; set; } = null!;

    public virtual MasterRecord MasterRecord { get; set; } = null!;
}
