using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IDeviceService
{
    Task<List<DeviceDto>> GetDevicesAsync(int? areaId = null, int? subAreaId = null);
    Task<DeviceDto?> GetDeviceByIdAsync(int id);
    Task<DeviceDto> CreateDeviceAsync(CreateDeviceDto dto);
    Task<bool> ToggleDeviceStatusAsync(int id);
    Task<bool> SyncDeviceAsync(int id);
    Task<(bool success, string message)> ActivateDeviceAsync(int id, string activationCode);
}
