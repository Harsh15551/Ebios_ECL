using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IDeviceService
{
    Task<List<DeviceDto>> GetDevicesAsync(int? areaId = null, int? subAreaId = null);
    Task<DeviceDto?> GetDeviceByIdAsync(int id);
    Task<DeviceDto> CreateDeviceAsync(
        CreateDeviceDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<DeviceDto> UpdateDeviceAsync(
        int id,
        UpdateDeviceDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<bool> ToggleDeviceStatusAsync(
        int id,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<(bool success, string message)> SyncDeviceAsync(int id);
    Task<(bool success, string message)> ActivateDeviceAsync(
        int id, 
        string activationCode,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
}
