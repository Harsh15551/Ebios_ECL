namespace Ebios.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }

    /// <summary>
    /// Category of operation: "Regularization", "SftpDispatch", "Security", "EmployeeMaster", "Device"
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Action performed: e.g. "REGULARIZATION_APPLIED", "REGULARIZATION_OVERWRITTEN", "SFTP_DELIVERED_VERIFIED", "SFTP_TRANSMISSION_FAILED", "2FA_RESET", etc.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Reference identifier, e.g. "EMP:ECL-7001 | Date:2026-07-30" or "ATTENDANCE_20261105_234500.csv"
    /// </summary>
    public string EntityReference { get; set; } = string.Empty;

    /// <summary>
    /// JSON snapshot of entity before modification (for diff tracking)
    /// </summary>
    public string? PreviousStateJson { get; set; }

    /// <summary>
    /// JSON snapshot of entity after modification
    /// </summary>
    public string? NewStateJson { get; set; }

    /// <summary>
    /// Human-readable explanation / reason
    /// </summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>
    /// Outcome status: "SUCCESS", "FAILED", "WARNING"
    /// </summary>
    public string Status { get; set; } = "SUCCESS";

    // Actor Identity
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string OfficerName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? IpAddress { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

