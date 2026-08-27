using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class LegalEntityRecord60
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public int? NumberOfRelatedPersons { get; set; }

    public int? NumberOfBeneficialOwners { get; set; }

    public string? Relation { get; set; }

    public string? CkycNumber { get; set; }

    public string? ControllingInterest { get; set; }

    public string? PercentageOwnership { get; set; }

    public string? OtherRelationName { get; set; }

    public string? Din { get; set; }
}
