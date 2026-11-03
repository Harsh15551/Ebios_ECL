using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IRosterService
{
    Task<List<EmployeeShiftRosterDto>> GetRosterAsync(DateTime date, int? areaId = null, int? subAreaId = null);
    Task<bool> AssignShiftAsync(AssignRosterDto dto);
    Task<int> BulkAssignRosterAsync(List<AssignRosterDto> dtoList);
    Task<EmployeeShiftRosterDto?> GetEmployeeShiftForDateAsync(string empCode, DateTime date);
}
