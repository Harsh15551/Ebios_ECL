namespace Ebios.Application.DTOs;

public record ShiftMasterDto(
    int Id,
    string ShiftCode,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan InWindowStart,
    TimeSpan InWindowEnd,
    TimeSpan OutWindowStart,
    TimeSpan OutWindowEnd,
    int GraceMinutes,
    double DurationHours,
    bool IsNightShift,
    bool IsActive
);

public record CreateShiftDto(
    string ShiftCode,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan InWindowStart,
    TimeSpan InWindowEnd,
    TimeSpan OutWindowStart,
    TimeSpan OutWindowEnd,
    int GraceMinutes = 30,
    double DurationHours = 8.0,
    bool IsNightShift = false
);

public record UpdateShiftDto(
    string ShiftCode,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan InWindowStart,
    TimeSpan InWindowEnd,
    TimeSpan OutWindowStart,
    TimeSpan OutWindowEnd,
    int GraceMinutes,
    double DurationHours,
    bool IsNightShift,
    bool IsActive = true
);

public record EmployeeShiftRosterDto(
    int Id,
    int EmployeeId,
    string EmpCode,
    string EmployeeName,
    string SubAreaName,
    DateTime RosterDate,
    int ShiftId,
    string ShiftCode,
    string ShiftName,
    string? AssignedBy,
    DateTime CreatedAt
);

public record AssignRosterDto(
    string EmpCode,
    DateTime RosterDate,
    string ShiftCode,
    string? AssignedBy = null
);

public record DailyMusterRollDto(
    int Id,
    DateTime AttendanceDate,
    string EmpCode,
    string? EmployeeName,
    int? EmployeeId,
    int? SubAreaId,
    string SubAreaName,
    string AreaName,
    string ShiftCode,
    string ShiftName,
    DateTime? FirstInTime,
    string? InSerialNumber,
    string? InDeviceName,
    DateTime? LastOutTime,
    string? OutSerialNumber,
    string? OutDeviceName,
    int TotalWorkMinutes,
    double TotalWorkHours,
    int OvertimeMinutes,
    int LateByMinutes,
    int EarlyExitMinutes,
    string Status,
    string RosterStatus,
    string? Remarks,
    bool IsVerified,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record MusterRollFilterDto(
    DateTime? Date = null,
    int? AreaId = null,
    int? SubAreaId = null,
    string? ShiftCode = null,
    string? Status = null,
    string? SearchTerm = null
);

public record MusterRollKpiDto(
    int TotalPunches,
    int PresentCount,
    int LateCount,
    int EarlyExitCount,
    int DoubleShiftCount,
    int SinglePunchCount
);

