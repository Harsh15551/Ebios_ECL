namespace Ebios.Domain.Entities;

public class Employee
{
    public int Id { get; set; }
    public string EmpCode { get; set; } = string.Empty; // Unique ECL Employee Code
    public string FullName { get; set; } = string.Empty;
    public string Designation { get; set; } = "General Mazdoor";

    // Linked to a SubArea / Colliery Unit (Primary Location)
    public int SubAreaId { get; set; }
    public SubArea? SubArea { get; set; }

    // Additional Locations / Colliery Units (comma-separated SubArea IDs, e.g. "2,3")
    public string? AdditionalSubAreaIds { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsSyncedToEbios { get; set; } = false;
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

