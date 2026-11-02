namespace Ebios.Domain.Entities;

public class SubArea
{
    public int Id { get; set; }
    public string SubAreaCode { get; set; } = string.Empty; // Maps to eBios LocationCode
    public string SubAreaName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Foreign Key: Area
    public int AreaId { get; set; }
    public Area? Area { get; set; }

    // eBios Sync tracking
    public bool IsSyncedToEbios { get; set; } = false;
    public DateTime? LastSyncedAt { get; set; }
}
