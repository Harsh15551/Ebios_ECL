using Ebios.Domain.Entities;

namespace Ebios.Application.DTOs;

public record UserDto(
    int Id,
    string Username,
    string FullName,
    string Email,
    UserRole Role,
    int? AreaId,
    string? AreaName,
    int? SubAreaId,
    string? SubAreaName,
    bool TwoFactorEnabled,
    bool IsActive,
    DateTime CreatedAt,
    bool CanManageEmployees = true,
    bool CanManageShifts = true
);

public record CreateUserDto(
    string Username,
    string FullName,
    string Email,
    string Password,
    UserRole Role,
    int? AreaId = null,
    int? SubAreaId = null,
    bool CanManageEmployees = true,
    bool CanManageShifts = true
);

public record UpdateUserPermissionsDto(
    int UserId,
    bool CanManageEmployees,
    bool CanManageShifts
);

public record LoginDto(
    string Username,
    string Password,
    string? TwoFactorCode = null
);

public record AuthResultDto(
    bool Success,
    string? ErrorMessage,
    UserDto? User = null,
    bool RequiresTwoFactor = false
);

public record TwoFactorSetupDto(
    string SharedKey,
    string FormattedSharedKey,
    string AuthenticatorUri,
    string QrCodeDataUrl
);

public record TwoFactorRecoveryCodesDto(
    List<string> RecoveryCodes
);

public record PasswordResetRequestDto(
    string UsernameOrEmail
);

public record PasswordResetConfirmDto(
    string UsernameOrEmail,
    string Pin,
    string NewPassword
);

public record ChangePasswordDto(
    string CurrentPassword,
    string NewPassword
);
