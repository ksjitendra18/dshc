using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class SearchResponse
{
    public long Id { get; set; }

    public long? SearchRequestId { get; set; }

    public string? ResponseFileName { get; set; }

    public int? ResponseFileNumber { get; set; }

    public int? LineNumber { get; set; }

    public int? InputRecordLineNumber { get; set; }

    public string? ClientType { get; set; }

    public string? SearchByOvdType { get; set; }

    public string? SearchByOvdNumber { get; set; }

    public string? SearchKey { get; set; }

    public string? CkycReferenceNumber { get; set; }

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string? LastName { get; set; }

    public string? Gender { get; set; }

    public string? MobileNumber { get; set; }

    public string? EmailAddress { get; set; }

    public string? LastUpdatedDate { get; set; }

    public string? Cin { get; set; }

    public string? LegalEntityName { get; set; }

    public string? PhotoReference { get; set; }

    public string? RegistrationDate { get; set; }

    public string? DeactivationReason { get; set; }

    public string? Remark { get; set; }

    public string? PanDocument { get; set; }

    public string? AadhaarDocument { get; set; }

    public string? PassportDocument { get; set; }

    public string? DrivingLicenseDocument { get; set; }

    public string? VoterIdDocument { get; set; }

    public string? NregaDocument { get; set; }

    public string? DisabilityDocument { get; set; }

    public string? Form6061Document { get; set; }

    public string? ForeignJurisdictionDocument { get; set; }

    public string? NprDocument { get; set; }

    public string? UtilityBillDocument { get; set; }

    public string? IncorporationDocument { get; set; }

    public string? MemorandumDocument { get; set; }

    public string? RegistrationCertificate { get; set; }

    public string? PartnershipDeed { get; set; }

    public string? TrustDeed { get; set; }

    public string? SupportingPoiDocument { get; set; }

    public string? OtherDocument { get; set; }

    public string? Filler1 { get; set; }

    public string? Filler2 { get; set; }

    public string? Filler3 { get; set; }

    public string? Filler4 { get; set; }

    public string? Filler5 { get; set; }

    public string? Filler6 { get; set; }

    public string? Filler7 { get; set; }

    public string? Filler8 { get; set; }

    public string? RecordLevelHash { get; set; }

    public string? RawResponseData { get; set; }

    public DateTime? CreatedAt { get; set; }
}
