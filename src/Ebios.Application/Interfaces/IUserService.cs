using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IUserService
{
    Task<List<UserDto>> GetUsersAsync(int? areaId = null, int? subAreaId = null);
    Task<UserDto?> GetUserByIdAsync(int id);
    Task<UserDto> CreateUserAsync(
        CreateUserDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<bool> ToggleUserStatusAsync(
        int id,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<bool> UpdateUserPermissionsAsync(
        UpdateUserPermissionsDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<AuthResultDto> AuthenticateAsync(LoginDto dto);
    Task<TwoFactorSetupDto> GenerateTwoFactorSetupAsync(int userId);
    Task<(bool Success, string Message, List<string>? RecoveryCodes)> EnableTwoFactorAsync(int userId, string code);
    Task<(bool Success, string Message)> DisableTwoFactorAsync(int userId, string code);
    Task<(bool Success, string Message)> AdminResetTwoFactorAsync(
        int targetUserId,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<AuthResultDto> VerifyTwoFactorLoginAsync(string username, string code);
    Task<(bool Success, string Message, string? MaskedEmail)> RequestPasswordResetOtpAsync(string usernameOrEmail);
    Task<(bool Success, string Message)> ConfirmPasswordResetAsync(string usernameOrEmail, string pin, string newPassword);
    Task<(bool Success, string Message, string? MaskedEmail)> RequestAdminEmergencyOtpAsync();
    Task<AuthResultDto> VerifyAdminEmergencyOtpAsync(string pin);
    Task<(bool Success, string Message)> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
