namespace Ebios.Domain.Entities;

public class Device
{
    public int Id { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Direction { get; set; } = "in"; // "in" (Entry) or "out" (Exit)
    public string DeviceType { get; set; } = "Biometric";
    public string TimeZone { get; set; } = "India Standard Time";
    public string ActivationCode { get; set; } = "0";

    // Linked to a SubArea / Colliery Unit
    public int SubAreaId { get; set; }
    public SubArea? SubArea { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsSyncedToEbios { get; set; } = false;
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

