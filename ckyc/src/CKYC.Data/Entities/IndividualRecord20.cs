using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class IndividualRecord20
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public string? SearchKey { get; set; }

    public string? KycType { get; set; }

    public string? NameTitle { get; set; }

    public string? NameFirst { get; set; }

    public string? NameMiddle { get; set; }

    public string? NameLast { get; set; }

    public string? MaidenTitle { get; set; }

    public string? MaidenFirst { get; set; }

    public string? MaidenMiddle { get; set; }

    public string? MaidenLast { get; set; }

    public string? MotherTitle { get; set; }

    public string? MotherFirst { get; set; }

    public string? MotherMiddle { get; set; }

    public string? MotherLast { get; set; }

    public string? FatherTitle { get; set; }

    public string? FatherFirst { get; set; }

    public string? FatherMiddle { get; set; }

    public string? FatherLast { get; set; }

    public string? SpouseTitle { get; set; }

    public string? SpouseFirst { get; set; }

    public string? SpouseMiddle { get; set; }

    public string? SpouseLast { get; set; }

    public string? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? ResidentialStatus { get; set; }

    public string? ResidentialSupportedByDocument { get; set; }

    public string? Nationality { get; set; }

    public string? NationalitySupportedByDocument { get; set; }

    public string? DifferentlyAbledStatus { get; set; }

    public string? DifferentlyAbledType { get; set; }

    public string? Pan { get; set; }

    public string? PanVerified { get; set; }

    public string? PhotoOfIndividual { get; set; }

    public string? Minor { get; set; }

    public string? DoBmatchWithOvd { get; set; }

    public string? NameMatchWithOvd { get; set; }

    public string? PhotoMatchWithOvd { get; set; }

    public string? GenderProvidedInOvd { get; set; }

    public string? GenderMatchWithOvd { get; set; }

    public string? Form97Provided { get; set; }

    public string? Form61Provided { get; set; }

    public string? PanDocument { get; set; }

    public string? OtherTypeOfImpairment { get; set; }

    public string? DisabilityReferenceNumber { get; set; }

    public string? PermanentDisability { get; set; }

    public string? DisabilityDate { get; set; }

    public string? PercentageOfImpairment { get; set; }

    public string? DifferentlyAbledSupportedByDocument { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
