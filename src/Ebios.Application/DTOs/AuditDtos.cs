namespace Ebios.Application.DTOs;

public record AuditLogDto(
    long Id,
    string Category,
    string Action,
    string EntityReference,
    string? PreviousStateJson,
    string? NewStateJson,
    string Details,
    string Status,
    int? UserId,
    string Username,
    string OfficerName,
    string Role,
    string? IpAddress,
    DateTime Timestamp
);

public record AuditFilterDto(
    string? Category = null,
    string? SearchText = null,
    string? Status = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int Page = 1,
    int PageSize = 25
);

public record AuditPagedResultDto(
    List<AuditLogDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public record LastSftpStatusDto(
    bool HasRun,
    DateTime? Timestamp,
    string Status, // "SUCCESS", "FAILED", "NONE"
    string? FileName,
    int RecordCount,
    long? FileSizeBytes,
    string? RemoteHost,
    string? RemotePath,
    string? ErrorMessage,
    string? TriggerType,
    string? OfficerName
);

