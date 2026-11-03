namespace Ebios.Domain.Entities;

public class AttendanceDailySummary
{
    public int Id { get; set; }
    
    // Shift business date (UTC Date)
    public DateTime AttendanceDate { get; set; }
    
    public string EmpCode { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    
    public int? SubAreaId { get; set; }
    public SubArea? SubArea { get; set; }
    
    // Shift Information
    public int? ShiftId { get; set; }
    public ShiftMaster? Shift { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    
    // First IN Punch Details
    public DateTime? FirstInTime { get; set; }
    public string? InSerialNumber { get; set; }
    public string? InDeviceName { get; set; }
    
    // Last OUT Punch Details
    public DateTime? LastOutTime { get; set; }
    public string? OutSerialNumber { get; set; }
    public string? OutDeviceName { get; set; }
    
    // Metrics
    public int TotalWorkMinutes { get; set; }
    public double TotalWorkHours { get; set; }
    public int OvertimeMinutes { get; set; }
    public int LateByMinutes { get; set; }
    public int EarlyExitMinutes { get; set; }
    
    // Attendance Evaluation:
    // Status: "Present", "Half Day", "Single Punch", "Late", "Overtime", "Absent"
    public string Status { get; set; } = "Single Punch";
    
    // Roster Status: "Assigned", "Auto-Matched", "Manual-Overridden"
    public string RosterStatus { get; set; } = "Auto-Matched";
    
    public string? Remarks { get; set; }
    public bool IsVerified { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

