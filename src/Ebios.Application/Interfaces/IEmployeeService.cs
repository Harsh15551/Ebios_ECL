using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IEmployeeService
{
    Task<List<EmployeeDto>> GetEmployeesAsync(int? areaId = null, int? subAreaId = null);
    Task<EmployeeDto?> GetEmployeeByIdAsync(int id);
    Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeDto dto);
    Task<(bool success, string message)> UpdateEmployeeAsync(int id, UpdateEmployeeDto dto, int? requestingUserAreaId = null);
    Task<(bool success, string message)> ToggleEmployeeStatusAsync(int id, string? reason = null, bool isBlacklist = false);
    Task<(int successCount, int totalCount, string message)> BulkBlacklistEmployeesAsync(List<BulkBlockEntryDto> entries, string reason, int? requestingUserAreaId = null, int? requestingUserSubAreaId = null);
    Task<(int successCount, int totalCount, List<string> errors)> BulkCreateEmployeesAsync(List<BulkEmployeeEntryDto> entries, int? defaultSubAreaId = null);
    Task<List<string>> GetDesignationsAsync();
    Task<(bool success, string message)> AddDesignationAsync(string title);
    Task<(bool success, string message)> UpdateDesignationAsync(string oldTitle, string newTitle);
    Task<(bool success, string message)> DeleteDesignationAsync(string title);
    Task<List<WorkerDeviceAccessDto>> GetWorkerDevicesAsync(int employeeId);
    Task<(bool success, string message)> ToggleWorkerDeviceAccessAsync(int employeeId, string deviceSerialNumber, bool block);
    Task<(bool success, string message)> BlockAllDevicesForWorkerAsync(int employeeId, bool blockAll);
    Task<(bool success, string message)> ToggleHigherAuthorityAsync(int employeeId, int? requestingUserAreaId = null);
    Task<bool> SyncEmployeeAsync(int id);
}
