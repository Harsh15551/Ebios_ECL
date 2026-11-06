using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IRosterService
{
    Task<List<EmployeeShiftRosterDto>> GetRosterAsync(DateTime date, int? areaId = null, int? subAreaId = null);
    Task<bool> AssignShiftAsync(
        AssignRosterDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null,
        bool skipAudit = false);
    Task<int> BulkAssignRosterAsync(
        List<AssignRosterDto> dtoList,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<EmployeeShiftRosterDto?> GetEmployeeShiftForDateAsync(string empCode, DateTime date);
}

