namespace Ebios.Application.DTOs;

public record SftpConfigDto(
    string Host,
    int Port,
    string UserName,
    string Password,
    string RemotePath,
    string DailyScheduledTime,
    bool IsEnabled
);

public record SftpExportResultDto(
    bool Success,
    string Message,
    string? RemoteFilePath = null,
    int RecordCount = 0,
    DateTime? TransmittedAt = null
);

