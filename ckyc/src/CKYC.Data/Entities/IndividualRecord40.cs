using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class IndividualRecord40
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? PermLine1 { get; set; }

    public string? PermLine2 { get; set; }

    public string? PermLine3 { get; set; }

    public string? PermCountry { get; set; }

    public string? PermState { get; set; }

    public string? PermDistrict { get; set; }

    public string? PermCity { get; set; }

    public string? PermPinCode { get; set; }

    public string? PermPinOthers { get; set; }

    public string? PermDigipin { get; set; }

    public string? PermSupportedDocument { get; set; }

    public string? PermMatchOvd { get; set; }

    public string? CurrSameAsPermanent { get; set; }

    public string? CurrLine1 { get; set; }

    public string? CurrLine2 { get; set; }

    public string? CurrLine3 { get; set; }

    public string? CurrCountry { get; set; }

    public string? CurrState { get; set; }

    public string? CurrDistrict { get; set; }

    public string? CurrCity { get; set; }

    public string? CurrPinCode { get; set; }

    public string? CurrPinOthers { get; set; }

    public string? CurrDigipin { get; set; }

    public string? CurrSupportedDocument { get; set; }

    public string? CurrMatchOvd { get; set; }

    public string? CurrProofOfAddress { get; set; }

    public string? CurrProofOfAddressType { get; set; }

    public string? CurrLengthOfAadhaar { get; set; }

    public string? CurrIdNumber { get; set; }

    public string? CurrAadhaarVerification { get; set; }

    public string? CurrOvdExpiryDate { get; set; }

    public string? CurrDeemedPoa { get; set; }

    public string? CurrDeemedPoaVerified { get; set; }

    public string? CurrCertifiedCopy { get; set; }

    public string? CurrEquivalentEdoc { get; set; }

    public string? CurrDigiLockerVerified { get; set; }

    public string? CurrRemoteGeoTagging { get; set; }

    public string? CurrAddressExactlyMatch { get; set; }

    public string? CurrPositiveVerification { get; set; }

    public string? CurrPhysicalThirdParty { get; set; }

    public string? CurrPhysicalReOfficial { get; set; }

    public string? CurrPresenceInRepository { get; set; }

    public string? CurrForeignGovDocument { get; set; }

    public string? CurrCopyOfOvd { get; set; }
}
