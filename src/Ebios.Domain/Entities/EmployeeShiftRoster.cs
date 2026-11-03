namespace Ebios.Domain.Entities;

public class EmployeeShiftRoster
{
    public int Id { get; set; }
    
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    
    public string EmpCode { get; set; } = string.Empty;
    public DateTime RosterDate { get; set; } // UTC midnight representing the shift business date
    
    public int ShiftId { get; set; }
    public ShiftMaster? Shift { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    
    public string? AssignedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
