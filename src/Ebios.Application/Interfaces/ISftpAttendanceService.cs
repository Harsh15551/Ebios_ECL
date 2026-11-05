using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface ISftpAttendanceService
{
    Task<string> GenerateAttendanceCsvAsync(DateTime attendanceDate, int? areaId = null, int? subAreaId = null);
    Task<SftpExportResultDto> ExportAndUploadDailyAttendanceAsync(DateTime attendanceDate, int? areaId = null, int? subAreaId = null);
    Task<(bool Success, string Message)> TestConnectionAsync();
}

