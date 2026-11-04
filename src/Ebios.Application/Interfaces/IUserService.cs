using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IUserService
{
    Task<List<UserDto>> GetUsersAsync(int? areaId = null, int? subAreaId = null);
    Task<UserDto?> GetUserByIdAsync(int id);
    Task<UserDto> CreateUserAsync(CreateUserDto dto);
    Task<bool> ToggleUserStatusAsync(int id);
    Task<bool> UpdateUserPermissionsAsync(UpdateUserPermissionsDto dto);
    Task<AuthResultDto> AuthenticateAsync(LoginDto dto);
}
