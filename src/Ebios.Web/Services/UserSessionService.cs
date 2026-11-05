using Ebios.Application.DTOs;
using Ebios.Domain.Entities;

namespace Ebios.Web.Services;

public class UserSessionService
{
    private static UserDto? _persistedUser;
    private UserDto? _currentUser = _persistedUser;

    public UserDto? CurrentUser => _currentUser ?? _persistedUser;
    public bool IsAuthenticated => (_currentUser ?? _persistedUser) != null;

    public bool IsSuperAdmin => CurrentUser?.Role == UserRole.SuperAdmin;
    public bool IsAreaAdmin => CurrentUser?.Role == UserRole.AreaPersonnelManager;
    public bool IsUnitAdmin => CurrentUser?.Role == UserRole.UnitPersonnelManager;

    public int? CurrentAreaId => CurrentUser?.AreaId;
    public int? CurrentSubAreaId => CurrentUser?.SubAreaId;
    public string? CurrentAreaName => CurrentUser?.AreaName;
    public string? CurrentSubAreaName => CurrentUser?.SubAreaName;
    public string CurrentFullName => CurrentUser?.FullName ?? "Guest";

    // Granular RBAC Permissions (Configured by SuperAdmin)
    public bool CanManageEmployees => IsSuperAdmin || (CurrentUser?.CanManageEmployees ?? false);
    public bool CanManageShifts => IsSuperAdmin || (CurrentUser?.CanManageShifts ?? false);

    // Indian Standard Time (IST: UTC + 05:30)
    public static DateTime GetIstToday()
    {
        try
        {
            var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone).Date;
        }
        catch
        {
            return DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;
        }
    }

    // Persist Attendance Date Range across navigations and tabs
    private static DateTime? _persistedFromDate;
    private static DateTime? _persistedToDate;

    public DateTime AttendanceFromDate
    {
        get => _persistedFromDate ?? GetIstToday().AddDays(-7);
        set => _persistedFromDate = value;
    }

    public DateTime AttendanceToDate
    {
        get => _persistedToDate ?? GetIstToday();
        set => _persistedToDate = value;
    }

    public event Action? OnChange;

    public void SetCurrentUser(UserDto? user)
    {
        _currentUser = user;
        _persistedUser = user;
        NotifyStateChanged();
    }

    public void Logout()
    {
        _currentUser = null;
        _persistedUser = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
