using System.Text.Json.Serialization;

namespace Ebios.Application.DTOs;

public record AttendanceLogDto(
    int Id,
    string EmpCode,
    string? EmployeeName,
    int? EmployeeId,
    DateTime LogDate,
    DateTime? DownloadDate,
    string SerialNumber,
    string? DeviceName,
    string Direction,
    string? VerificationType,
    int? SubAreaId,
    string? SubAreaName,
    string? AreaName,
    string Status,
    bool IsFlagged,
    DateTime CreatedAt
);

public class WebhookPunchItem
{
    [JsonPropertyName("EmployeeCode")]
    public string EmployeeCode { get; set; } = string.Empty;

    [JsonPropertyName("EmployeeName")]
    public string? EmployeeName { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonIgnore]
    public string? ResolvedName => !string.IsNullOrWhiteSpace(EmployeeName) ? EmployeeName.Trim() : (!string.IsNullOrWhiteSpace(Name) ? Name.Trim() : null);

    [JsonPropertyName("DownloadDate")]
    public string? DownloadDate { get; set; }

    [JsonPropertyName("LogDate")]
    public string LogDate { get; set; } = string.Empty;

    [JsonPropertyName("DeviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("SerialNumber")]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyName("Direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("DeviceDirection")]
    public string? DeviceDirection { get; set; }

    [JsonPropertyName("WorkCode")]
    public string? WorkCode { get; set; }

    [JsonPropertyName("VerificationType")]
    public string? VerificationType { get; set; }

    [JsonPropertyName("GPS")]
    public string? GPS { get; set; }
}

public class WebhookEncryptedPayload
{
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

public record AttendanceFilterDto(
    int? AreaId = null,
    int? SubAreaId = null,
    string? EmpCode = null,
    string? Status = null, // "All", "Verified", "Flagged", "Dismissed"
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? SearchTerm = null
);

