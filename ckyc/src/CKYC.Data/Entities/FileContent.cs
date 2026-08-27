using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class FileContent
{
    public long Id { get; set; }

    public string Sha256 { get; set; } = null!;

    public byte[] Content { get; set; } = null!;

    public long ByteLength { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<IndividualDocument> IndividualDocuments { get; set; } = new List<IndividualDocument>();

    public virtual ICollection<LegalEntityDocument> LegalEntityDocuments { get; set; } = new List<LegalEntityDocument>();
}
