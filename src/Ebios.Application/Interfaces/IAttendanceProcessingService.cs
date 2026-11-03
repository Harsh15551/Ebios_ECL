using Ebios.Application.DTOs;
using Ebios.Domain.Entities;

namespace Ebios.Application.Interfaces;

public interface IAttendanceProcessingService
{
    Task ProcessPunchIntoMusterRollAsync(AttendanceLog punch);
    Task<int> RecalculateDailyMusterRollAsync(DateTime date, int? subAreaId = null);
    Task<List<DailyMusterRollDto>> GetMusterRollReportAsync(MusterRollFilterDto filter, int? userAreaId = null, int? userSubAreaId = null);
    Task<MusterRollKpiDto> GetMusterRollKpisAsync(DateTime date, int? userAreaId = null, int? userSubAreaId = null);
}
