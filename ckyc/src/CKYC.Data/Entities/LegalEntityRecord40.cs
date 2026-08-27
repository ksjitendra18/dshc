using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class LegalEntityRecord40
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? RegLine1 { get; set; }

    public string? RegLine2 { get; set; }

    public string? RegLine3 { get; set; }

    public string? RegCity { get; set; }

    public string? RegState { get; set; }

    public string? RegDistrict { get; set; }

    public string? RegPinCode { get; set; }

    public string? RegPinOthers { get; set; }

    public string? RegDigipin { get; set; }

    public string? RegCountry { get; set; }

    public string? RegProofOfAddress { get; set; }

    public string? RegOtherDocumentName { get; set; }

    public string? RegDocument { get; set; }

    public string? SameAsRegistered { get; set; }

    public string? PrinLine1 { get; set; }

    public string? PrinLine2 { get; set; }

    public string? PrinLine3 { get; set; }

    public string? PrinCity { get; set; }

    public string? PrinState { get; set; }

    public string? PrinDistrict { get; set; }

    public string? PrinPinCode { get; set; }

    public string? PrinPinOthers { get; set; }

    public string? PrinDigipin { get; set; }

    public string? PrinCountry { get; set; }

    public string? PrinProofOfAddress { get; set; }

    public string? PrinOtherDocumentName { get; set; }

    public string? PrinDocument { get; set; }
}
