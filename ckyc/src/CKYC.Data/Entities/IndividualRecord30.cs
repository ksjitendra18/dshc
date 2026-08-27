using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class IndividualRecord30
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? OvdType { get; set; }

    public string? ModeOfAadhaarVerification { get; set; }

    public string? PassportExpiryDate { get; set; }

    public string? DrivingLicenseExpiryDate { get; set; }

    public string? LengthOfAadhaar { get; set; }

    public string? IdNumber { get; set; }

    public string? CertifiedCopyWithOriginal { get; set; }

    public string? EquivalentEdoc { get; set; }

    public string? VerifiedFromDigiLocker { get; set; }

    public string? PresenceInMeaRepository { get; set; }

    public string? PresenceInEciRepository { get; set; }

    public string? PresenceInRtoRepository { get; set; }

    public string? PresenceInNregaRepository { get; set; }

    public string? PresenceInNprRecords { get; set; }

    public string? DataFromOfflineVerification { get; set; }

    public string? ModeOfAuthentication { get; set; }

    public string? EkycDataFromUidai { get; set; }

    public string? CopyOfOvd { get; set; }
}
