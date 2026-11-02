namespace Ebios.Application.DTOs;

public record DeviceDto(
    int Id,
    string SerialNumber,
    string DeviceName,
    string Direction,
    string DeviceType,
    string ActivationCode,
    int SubAreaId,
    string SubAreaName,
    string AreaName,
    bool IsActive,
    bool IsSyncedToEbios,
    DateTime? LastSyncedAt,
    DateTime CreatedAt
);

public record CreateDeviceDto(
    string SerialNumber,
    string DeviceName,
    string Direction,
    string DeviceType,
    string ActivationCode,
    int SubAreaId
);
