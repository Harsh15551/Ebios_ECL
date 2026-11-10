namespace Ebios.Domain.Entities;

public class EmployeeBiometricForm
{
    public int Id { get; set; }

    // Relationship to Employee
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // Unique ECL Employee Identifier
    public string EmpCode { get; set; } = string.Empty;

    // SECTION 1: DETAILS OF THE PERSON APPEARED AT REGISTRATION
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

    // SECTION 2: RELEVANT PHOTOGRAPHS (Stored locally on server disk, only relative path in DB)
    public string? AadhaarPhotoPath { get; set; }
    public string? IdCardPhotoPath { get; set; }
    public string? PersonPhotoPath { get; set; }
    public string? TemplatePhotoPath { get; set; }

    // Uploaded Manually Completed & Signed Hardcopy Form (PDF or JPG)
    public string? SignedPhysicalFormPath { get; set; }

    // SECTION 3: PERSONNEL PRESENT DURING REGISTRATION (From ECL and WEBEL)
    // 1. HR (ECL)
    public string? EclHrName { get; set; }
    public string? EclHrDesignation { get; set; }
    public string EclHrDepartment { get; set; } = "HR";
    public string EclHrOrganization { get; set; } = "ECL";

    // 2. Security (ECL)
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
    public bool IsCompleted { get; set; } = false;
    public bool HasAadhaarConsent { get; set; } = true;
    public DateTime? ConsentTimestamp { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

