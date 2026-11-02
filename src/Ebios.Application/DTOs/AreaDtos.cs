namespace Ebios.Application.DTOs;

public record AreaDto(
    int Id,
    string AreaCode,
    string AreaName,
    string Headquarters,
    bool IsActive,
    int SubAreaCount,
    DateTime CreatedAt
);

public record CreateAreaDto(
    string AreaCode,
    string AreaName,
    string Headquarters = "ECL HQ"
);

public record SubAreaDto(
    int Id,
    string SubAreaCode,
    string SubAreaName,
    int AreaId,
    string AreaName,
    bool IsSyncedToEbios,
    DateTime? LastSyncedAt,
    bool IsActive,
    DateTime CreatedAt
);

public record CreateSubAreaDto(
    string SubAreaCode,
    string SubAreaName,
    int AreaId
);
