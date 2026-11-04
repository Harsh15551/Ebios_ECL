namespace Ebios.Application.DTOs;

public class BiometricFormDto
{
    public int Id { get; set; }
    public int? EmployeeId { get; set; }
    public string EmpCode { get; set; } = string.Empty;

    // Section 1: Details of the Person Appeared at Registration
    public string EmployeeName { get; set; } = string.Empty;
    public string AadhaarNumber { get; set; } = string.Empty;
    public string Category { get; set; } = "Non-Executive"; // "Executive" / "Non-Executive"
    public string PositionDesignation { get; set; } = string.Empty;
    public string DepartmentUnit { get; set; } = string.Empty;
    public string PlaceOfPosting { get; set; } = string.Empty;
    public DateTime? RegistrationDateTime { get; set; }
    public string RegistrationLocation { get; set; } = string.Empty;
    public string ControllingOfficerName { get; set; } = string.Empty;
    public string ControllingOfficerDesignation { get; set; } = string.Empty;

    // Section 2: Relative Photograph URLs / Endpoints
    public string? AadhaarPhotoPath { get; set; }
    public string? IdCardPhotoPath { get; set; }
    public string? PersonPhotoPath { get; set; }
    public string? TemplatePhotoPath { get; set; }
    public string? SignedPhysicalFormPath { get; set; }

    public string? AadhaarPhotoUrl => !string.IsNullOrEmpty(AadhaarPhotoPath) ? $"/api/biometric-forms/photo/{EmpCode}/aadhaar?t={PhotoCacheKey}" : null;
    public string? IdCardPhotoUrl => !string.IsNullOrEmpty(IdCardPhotoPath) ? $"/api/biometric-forms/photo/{EmpCode}/idcard?t={PhotoCacheKey}" : null;
    public string? PersonPhotoUrl => !string.IsNullOrEmpty(PersonPhotoPath) ? $"/api/biometric-forms/photo/{EmpCode}/person?t={PhotoCacheKey}" : null;
    public string? TemplatePhotoUrl => !string.IsNullOrEmpty(TemplatePhotoPath) ? $"/api/biometric-forms/photo/{EmpCode}/template?t={PhotoCacheKey}" : null;
    public string? SignedPhysicalFormUrl => !string.IsNullOrEmpty(SignedPhysicalFormPath) ? $"/api/biometric-forms/signed-form/{EmpCode}" : null;

    public long PhotoCacheKey { get; set; } = DateTime.UtcNow.Ticks;

    // Section 3: Personnel Present During Registration (ECL and WEBEL)
    // 1. ECL HR
    public string? EclHrName { get; set; }
    public string? EclHrDesignation { get; set; }
    public string EclHrDepartment { get; set; } = "HR";
    public string EclHrOrganization { get; set; } = "ECL";

    // 2. ECL Security
    public string? EclSecurityName { get; set; }
    public string? EclSecurityDesignation { get; set; }
    public string EclSecurityDepartment { get; set; } = "Security";
    public string EclSecurityOrganization { get; set; } = "ECL";

    // 3. WEBEL
    public string? WebelName { get; set; }
    public string? WebelDesignation { get; set; }
    public string? WebelDepartment { get; set; }
    public string WebelOrganization { get; set; } = "WEBEL";

    // 4. ECL / WEBEL
    public string? Witness4Name { get; set; }
    public string? Witness4Designation { get; set; }
    public string? Witness4Department { get; set; }
    public string Witness4Organization { get; set; } = "ECL / WEBEL";

    // Status & Audit
    public bool IsCompleted { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class BulkBiometricImportResult
{
    public int TotalProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> SuccessSummaries { get; set; } = new();
}

