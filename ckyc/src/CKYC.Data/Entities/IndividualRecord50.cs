using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class IndividualRecord50
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? EmailAddress { get; set; }

    public string? CountryCode { get; set; }

    public string? MobileNumber { get; set; }

    public string? MobileValidatedViaOtp { get; set; }

    public string? EmailValidatedViaOtp { get; set; }

    public string? MobileValidatedViaThirdParty { get; set; }
}
