namespace Ebios.Domain.Entities;

public class AttendanceLog
{
    public int Id { get; set; }
    public string EmpCode { get; set; } = string.Empty;
    public string? RawEmployeeName { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    
    public DateTime LogDate { get; set; }
    public DateTime? DownloadDate { get; set; }
    
    public string SerialNumber { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public int? DeviceId { get; set; }
    public Device? Device { get; set; }
    
    public string Direction { get; set; } = "IN";
    public string? DeviceDirection { get; set; }
    public string? VerificationType { get; set; }
    public string? WorkCode { get; set; }
    public string? Gps { get; set; }
    
    public int? SubAreaId { get; set; }
    public SubArea? SubArea { get; set; }
    
    public string Status { get; set; } = "Verified"; // Verified, Flagged, Dismissed
    public bool IsFlagged { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

