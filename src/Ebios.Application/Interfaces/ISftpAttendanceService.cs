using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface ISftpAttendanceService
{
    Task<string> GenerateAttendanceCsvAsync(DateTime attendanceDate, int? areaId = null, int? subAreaId = null);
    Task<SftpExportResultDto> ExportAndUploadDailyAttendanceAsync(
        DateTime attendanceDate,
        int? areaId = null,
        int? subAreaId = null,
        string triggerType = "MANUAL",
        int? userId = null,
        string username = "System",
        string officerName = "System Worker",
        string role = "System");
    Task<(bool Success, string Message)> TestConnectionAsync();
    string GetDailyScheduleTime();
    Task<(bool Success, string Message)> SetDailyScheduleTimeAsync(
        string newScheduleTime,
        int? userId = null,
        string username = "System",
        string officerName = "System Worker",
        string role = "System");
}

