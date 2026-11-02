using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IAttendanceService
{
    Task<List<AttendanceLogDto>> GetLogsAsync(AttendanceFilterDto filter, int? userAreaId = null, int? userSubAreaId = null);
    Task<List<AttendanceLogDto>> GetEmployeeLogsAsync(string empCode, DateTime? fromDate = null, DateTime? toDate = null);
    Task<int> ProcessWebhookPunchesAsync(List<WebhookPunchItem> punches);
    Task<bool> UpdateLogStatusAsync(int logId, string status);
    Task<bool> DismissFlaggedPunchesAsync(string empCode);
    Task<int> GetFlaggedCountAsync(int? areaId = null, int? subAreaId = null);
    Task<bool> LinkPunchesToEmployeeAsync(string empCode, int employeeId);
}

