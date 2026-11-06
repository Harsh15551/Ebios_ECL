using Ebios.Application.DTOs;
using Ebios.Domain.Entities;

namespace Ebios.Application.Interfaces;

public interface IAttendanceProcessingService
{
    Task ProcessPunchIntoMusterRollAsync(AttendanceLog punch);
    Task<int> RecalculateDailyMusterRollAsync(DateTime fromDate, DateTime toDate, int? subAreaId = null);
    Task<List<DailyMusterRollDto>> GetMusterRollReportAsync(MusterRollFilterDto filter, int? userAreaId = null, int? userSubAreaId = null);
    Task<MusterRollKpiDto> GetMusterRollKpisAsync(DateTime fromDate, DateTime toDate, int? userAreaId = null, int? userSubAreaId = null);
    Task<(bool Success, string Message)> RegularizeAttendanceAsync(RegularizeAttendanceDto dto, int? callerAreaId = null, int? callerSubAreaId = null);
    Task<(bool Success, string Message)> RevertToMachinePunchesAsync(int summaryId, int? callerAreaId = null, int? callerSubAreaId = null);
    Task<BulkRegularizeResultDto> BulkRegularizeAsync(List<BulkRegularizeEntryDto> entries, string adminUsername, int? callerAreaId = null, int? callerSubAreaId = null);
}

