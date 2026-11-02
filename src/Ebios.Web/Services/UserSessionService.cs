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
