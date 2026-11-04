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
    string? Designation,
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
    int EarlyInMinutes,
    int EarlyExitMinutes,
    string Status,
    string RosterStatus,
    string? Remarks,
    bool IsVerified,
    bool IsFlagged,
    bool IsHigherAuthority,
    bool IsManuallyCorrected,
    string? CorrectionReason,
    string? CorrectedBy,
    DateTime? CorrectedAt,
    bool IsSyncedToSap,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record RegularizeAttendanceDto(
    int SummaryId,
    DateTime? InTime,
    DateTime? OutTime,
    string Reason,
    string CorrectedBy,
    string? ShiftCode = null
);

public record BulkRegularizeEntryDto(
    string EmpCode,
    DateTime Date,
    DateTime? InTime,
    DateTime? OutTime,
    string Reason,
    string? ShiftCode = null
);

public record BulkRegularizeResultDto(
    int SuccessCount,
    int TotalCount,
    List<string> Errors
);

public record MusterRollFilterDto(
    DateTime? Date = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int? AreaId = null,
    int? SubAreaId = null,
    string? ShiftCode = null,
    string? Status = null,
    string? SearchTerm = null,
    bool IncludeFlagged = true
);

public record MusterRollKpiDto(
    int TotalPunches,
    int PresentCount,
    int LateCount,
    int EarlyExitCount,
    int DoubleShiftCount,
    int SinglePunchCount
);

