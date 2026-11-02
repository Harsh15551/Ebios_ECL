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
    bool IsSyncedToEbios,
    DateTime? LastSyncedAt,
    DateTime CreatedAt
);

public record CreateEmployeeDto(
    string EmpCode,
    string FullName,
    string Designation,
    int SubAreaId,
    List<int>? AdditionalSubAreaIds = null
);

public record UpdateEmployeeDto(
    string FullName,
    string Designation,
    int SubAreaId,
    List<int>? AdditionalSubAreaIds = null
);
