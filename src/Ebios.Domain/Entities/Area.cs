namespace Ebios.Domain.Entities;

public class Area
{
    public int Id { get; set; }
    public string AreaCode { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public string Headquarters { get; set; } = "ECL HQ";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property: One Area has Many SubAreas
    public ICollection<SubArea> SubAreas { get; set; } = new List<SubArea>();
}
