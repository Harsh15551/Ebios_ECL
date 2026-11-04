namespace Ebios.Domain.Entities;

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    // Area mapping: Set if user is an APM
    public int? AreaId { get; set; }
    public Area? Area { get; set; }

    // SubArea mapping: Set if user is a UPM
    public int? SubAreaId { get; set; }
    public SubArea? SubArea { get; set; }

    // Security & 2FA
    public bool TwoFactorEnabled { get; set; } = false;
    public string? TwoFactorSecret { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Granular Permissions (Admin Access Control)
    public bool CanManageEmployees { get; set; } = true;
    public bool CanManageShifts { get; set; } = true;
}
