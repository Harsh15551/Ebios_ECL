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
    Task<TwoFactorSetupDto> GenerateTwoFactorSetupAsync(int userId);
    Task<(bool Success, string Message, List<string>? RecoveryCodes)> EnableTwoFactorAsync(int userId, string code);
    Task<(bool Success, string Message)> DisableTwoFactorAsync(int userId, string code);
    Task<(bool Success, string Message)> AdminResetTwoFactorAsync(int targetUserId);
    Task<AuthResultDto> VerifyTwoFactorLoginAsync(string username, string code);
    Task<(bool Success, string Message, string? MaskedEmail)> RequestPasswordResetOtpAsync(string usernameOrEmail);
    Task<(bool Success, string Message)> ConfirmPasswordResetAsync(string usernameOrEmail, string pin, string newPassword);
    Task<(bool Success, string Message, string? MaskedEmail)> RequestAdminEmergencyOtpAsync();
    Task<AuthResultDto> VerifyAdminEmergencyOtpAsync(string pin);
    Task<(bool Success, string Message)> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
