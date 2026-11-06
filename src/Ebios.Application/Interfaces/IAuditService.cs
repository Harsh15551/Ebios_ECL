using Ebios.Application.DTOs;
using Ebios.Domain.Entities;

namespace Ebios.Application.Interfaces;

public interface IAuditService
{
    Task LogAsync(AuditLog entry);

    Task LogRegularizationAsync(
        string empCode,
        DateTime attendanceDate,
        string? oldStateJson,
        string newStateJson,
        bool isOverwrite,
        string details,
        int? userId,
        string username,
        string officerName,
        string role);

    Task LogSftpTransmissionAsync(
        string fileName,
        DateTime targetDate,
        int recordCount,
        string status,
        string? error,
        long? remoteSizeBytes,
        string remoteHost,
        int remotePort,
        string remotePath,
        string triggerType,
        int? userId,
        string username,
        string officerName,
        string role);

    Task LogSecurityActionAsync(
        string action,
        string reference,
        string details,
        int? userId,
        string username,
        string officerName,
        string role,
        string? oldJson = null,
        string? newJson = null);

    Task<LastSftpStatusDto> GetLastSftpStatusAsync();

    Task<List<AuditLogDto>> GetRecentSftpLogsAsync(int count = 10);

    Task<List<AuditLogDto>> GetRecordAuditHistoryAsync(string entityReference);

    Task<AuditPagedResultDto> GetAuditLogsPagedAsync(AuditFilterDto filter);
}

