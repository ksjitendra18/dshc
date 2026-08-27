using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class LegalEntityRecord70
{
    public long Id { get; set; }

    public long? MasterRecordId { get; set; }

    public string? CustomerId { get; set; }

    public int? Record20LineNumber { get; set; }

    public string? Remarks { get; set; }

    public string? CertifiedCopies { get; set; }

    public string? EquivalentEdoc { get; set; }

    public string? VerificationFromDigiLocker { get; set; }

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

    public string? ConsentDocument { get; set; }

    public string? Place { get; set; }

    public string? DeclarationDate { get; set; }
}
