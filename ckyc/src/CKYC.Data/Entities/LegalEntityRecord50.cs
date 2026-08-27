using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class LegalEntityRecord50
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? CountryCode1 { get; set; }

    public string? MobileNumber1 { get; set; }

    public string? CountryCode2 { get; set; }

    public string? MobileNumber2 { get; set; }

    public string? EmailId1 { get; set; }

    public string? EmailId2 { get; set; }

    public string? Telephone { get; set; }

    public string? Fax { get; set; }
}
