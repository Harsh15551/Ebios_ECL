namespace Ebios.Application.Interfaces;

public interface IEmailService
{
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string username, string pin);
    Task<bool> SendAdminEmergencyLoginEmailAsync(string toEmail, string pin);
}

