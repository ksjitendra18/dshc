using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class IndividualRecord60
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? RelatedPersonType { get; set; }

    public string? CkycNumberOfRelatedPerson { get; set; }
}
