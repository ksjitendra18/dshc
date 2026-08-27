using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class LegalEntityRecord20
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public string? SearchKey { get; set; }

    public string? EntityName { get; set; }

    public string? EntityConstitution { get; set; }

    public string? ListedCompany { get; set; }

    public string? RegisteredFirm { get; set; }

    public string? RegisteredTrust { get; set; }

    public string? DateOfIncorporation { get; set; }

    public string? DateOfCommencement { get; set; }

    public string? PlaceOfIncorporation { get; set; }

    public string? CountryOfIncorporation { get; set; }

    public string? TinIssuingCountry { get; set; }

    public string? Pan { get; set; }

    public string? Form97 { get; set; }

    public string? TinGstNumber { get; set; }

    public string? PanDocument { get; set; }

    public string? PanVerified { get; set; }

    public string? TinGstnDocument { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
