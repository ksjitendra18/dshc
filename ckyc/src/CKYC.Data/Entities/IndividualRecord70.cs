using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class IndividualRecord70
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? Remarks { get; set; }

    public string? VideoKycWithoutOfficial { get; set; }

    public string? VideoKycWithReOfficial { get; set; }

    public string? FaceToFaceWithReOfficial { get; set; }

    public string? NonFaceToFace { get; set; }

    public string? FaceToFaceWithNonOfficial { get; set; }

    public string? AttestationDate { get; set; }

    public string? EmployeeName { get; set; }

    public string? EmployeeCode { get; set; }

    public string? EmployeeDesignation { get; set; }

    public string? EmployeeBranch { get; set; }

    public string? EmployeeCkycId { get; set; }

    public string? InstitutionName { get; set; }

    public string? InstitutionCode { get; set; }

    public string? DeclarationDocument { get; set; }

    public string? DeclarationFlag { get; set; }

    public string? ClientConsent { get; set; }

    public string? Place { get; set; }

    public string? DeclarationDate { get; set; }
}
