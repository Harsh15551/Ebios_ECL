namespace Ebios.Application.DTOs;

public record EmployeeDto(
    int Id,
    string EmpCode,
    string FullName,
    string Designation,
    int SubAreaId,
    string SubAreaName,
    string AreaName,
    List<int> AdditionalSubAreaIds,
    List<string> AdditionalSubAreaNames,
    bool IsActive,
    bool IsBlacklisted,
    bool IsHigherAuthority,
    string? StatusReason,
    string? BlockedDeviceSerials,
    bool IsSyncedToEbios,
    DateTime? LastSyncedAt,
    DateTime CreatedAt
);

public record WorkerDeviceAccessDto(
    int DeviceId,
    string SerialNumber,
    string DeviceName,
    string SubAreaName,
    string Direction,
    bool IsBlocked
);

public record BulkBlockEntryDto(
    string EmpCode,
    string? DeviceSerialNumber = null
);

public record BulkEmployeeEntryDto(
    string EmpCode,
    string FullName,
    string Designation,
    string? CollieryNameOrId = null,
    bool IsHigherAuthority = false
);

public record CreateEmployeeDto(
    string EmpCode,
    string FullName,
    string Designation,
    int SubAreaId,
    List<int>? AdditionalSubAreaIds = null,
    bool IsHigherAuthority = false
);

public record UpdateEmployeeDto(
    string FullName,
    string Designation,
    int SubAreaId,
    List<int>? AdditionalSubAreaIds = null,
    bool IsHigherAuthority = false
);
