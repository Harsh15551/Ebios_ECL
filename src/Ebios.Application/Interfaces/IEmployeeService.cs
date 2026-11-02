using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IEmployeeService
{
    Task<List<EmployeeDto>> GetEmployeesAsync(int? areaId = null, int? subAreaId = null);
    Task<EmployeeDto?> GetEmployeeByIdAsync(int id);
    Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeDto dto);
    Task<(bool success, string message)> UpdateEmployeeAsync(int id, UpdateEmployeeDto dto, int? requestingUserAreaId = null);
    Task<bool> ToggleEmployeeStatusAsync(int id);
    Task<bool> SyncEmployeeAsync(int id);
}
