namespace Ebios.Domain.Entities;

public class ShiftMaster
{
    public int Id { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    
    // Shift Working Timings
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    
    // Shift Detection Windows
    public TimeSpan InWindowStart { get; set; }
    public TimeSpan InWindowEnd { get; set; }
    public TimeSpan OutWindowStart { get; set; }
    public TimeSpan OutWindowEnd { get; set; }
    
    // Policy
    public int GraceMinutes { get; set; } = 30;
    public double DurationHours { get; set; } = 8.0;
    public bool IsNightShift { get; set; }
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

