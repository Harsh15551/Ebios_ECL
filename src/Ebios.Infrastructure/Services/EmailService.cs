using System.Net;
using System.Net.Mail;
using Ebios.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private (string host, int port, string user, string pass)? GetSmtpConfig()
    {
        string? host = Environment.GetEnvironmentVariable("SMTP_HOST")
                       ?? _configuration["SMTP_HOST"]
                       ?? "smtp.gmail.com";

        string? portStr = Environment.GetEnvironmentVariable("SMTP_PORT")
                          ?? _configuration["SMTP_PORT"]
                          ?? "587";

        int.TryParse(portStr, out int port);
        if (port <= 0) port = 587;

        string? user = Environment.GetEnvironmentVariable("SMTP_USER")
                       ?? _configuration["SMTP_USER"]
                       ?? _configuration["GMAIL_SMTP_SENDER"];

        string? rawPass = Environment.GetEnvironmentVariable("SMTP_PASS")
                          ?? _configuration["SMTP_PASS"]
                          ?? _configuration["GMAIL_SMTP_APP_PASSWORD"];

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(rawPass))
        {
            _logger.LogWarning("SMTP credentials not fully configured. Email cannot be dispatched.");
            return null;
        }

        // Clean any spaces from Google App Password (e.g. "abcd efgh ijkl mnop" -> "abcdefghijklmnop")
        string pass = rawPass.Replace(" ", "").Trim();

        return (host, port, user.Trim(), pass);
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string username, string pin)
    {
        var config = GetSmtpConfig();
        if (config == null) return false;

        string subject = "🔐 ECL eBios - Password Reset Verification Code";
        string htmlBody = $@"
<div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 580px; margin: 0 auto; background-color: #ffffff; border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.05);"">
    <div style=""background: linear-gradient(135deg, #1e3a8a 0%, #0d9488 100%); padding: 24px; text-align: center; color: #ffffff;"">
        <h2 style=""margin: 0 0 6px 0; font-size: 22px; font-weight: 700;"">Eastern Coalfields Limited</h2>
        <p style=""margin: 0; font-size: 13px; opacity: 0.9;"">Central Biometric Attendance Portal</p>
    </div>
    <div style=""padding: 30px 24px; color: #333333;"">
        <p style=""font-size: 16px; margin-top: 0;"">Hello <strong>{WebUtility.HtmlEncode(username)}</strong>,</p>
        <p style=""font-size: 14px; line-height: 1.6; color: #555555;"">
            You recently requested to reset your password for the ECL eBios Biometric System. Use the 6-digit verification code below to set a new password:
        </p>
        <div style=""text-align: center; margin: 28px 0;"">
            <div style=""display: inline-block; background-color: #f1f5f9; border: 2px dashed #0d9488; padding: 14px 28px; border-radius: 8px;"">
                <span style=""font-family: Consolas, 'Courier New', monospace; font-size: 32px; font-weight: 700; letter-spacing: 8px; color: #0f172a;"">{pin}</span>
            </div>
        </div>
        <p style=""font-size: 13px; color: #64748b; line-height: 1.5;"">
            ⏱️ <strong>This code is valid for 15 minutes.</strong><br/>
            ⚠️ If you did not request a password reset, please ignore this email or contact your IT Administrator immediately.
        </p>
    </div>
    <div style=""background-color: #f8fafc; padding: 16px 24px; text-align: center; font-size: 12px; color: #94a3b8; border-top: 1px solid #e2e8f0;"">
        Eastern Coalfields Limited &bull; Coal India Subsidiary &bull; Automated Security Dispatcher
    </div>
</div>";

        return await SendEmailAsync(config.Value, toEmail, subject, htmlBody);
    }

    public async Task<bool> SendAdminEmergencyLoginEmailAsync(string toEmail, string pin)
    {
        var config = GetSmtpConfig();
        if (config == null) return false;

        string subject = "🚨 ECL eBios - SuperAdmin Emergency Access PIN";
        string htmlBody = $@"
<div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 580px; margin: 0 auto; background-color: #ffffff; border: 1px solid #fecaca; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(220, 38, 38, 0.08);"">
    <div style=""background: linear-gradient(135deg, #b91c1c 0%, #7f1d1d 100%); padding: 24px; text-align: center; color: #ffffff;"">
        <h2 style=""margin: 0 0 6px 0; font-size: 22px; font-weight: 700;"">🚨 Emergency Break-Glass Access</h2>
        <p style=""margin: 0; font-size: 13px; opacity: 0.9;"">ECL Central Biometric Portal &bull; SuperAdmin Security Override</p>
    </div>
    <div style=""padding: 30px 24px; color: #333333;"">
        <p style=""font-size: 15px; margin-top: 0;"">Attention <strong>ECL Administrator</strong>,</p>
        <p style=""font-size: 14px; line-height: 1.6; color: #555555;"">
            An emergency passwordless bypass login was initiated for the root <strong>SuperAdmin</strong> account on the eBios Attendance Portal.
        </p>
        <div style=""text-align: center; margin: 28px 0;"">
            <div style=""display: inline-block; background-color: #fef2f2; border: 2px dashed #dc2626; padding: 14px 28px; border-radius: 8px;"">
                <span style=""font-family: Consolas, 'Courier New', monospace; font-size: 32px; font-weight: 700; letter-spacing: 8px; color: #991b1b;"">{pin}</span>
            </div>
        </div>
        <p style=""font-size: 13px; color: #64748b; line-height: 1.5;"">
            ⏱️ <strong>This emergency code is valid for 15 minutes.</strong><br/>
            🔒 Entering this code grants direct administrative control to the ECL Central Portal. If you did not initiate this request, take immediate defensive measures.
        </p>
    </div>
    <div style=""background-color: #f8fafc; padding: 16px 24px; text-align: center; font-size: 12px; color: #94a3b8; border-top: 1px solid #e2e8f0;"">
        Eastern Coalfields Limited &bull; System Defense &amp; Security Services
    </div>
</div>";

        return await SendEmailAsync(config.Value, toEmail, subject, htmlBody);
    }

    private async Task<bool> SendEmailAsync((string host, int port, string user, string pass) config, string toEmail, string subject, string htmlBody)
    {
        try
        {
            using var client = new SmtpClient(config.host, config.port)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(config.user, config.pass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000
            };

            using var message = new MailMessage
            {
                From = new MailAddress(config.user, "ECL eBios Attendance Security"),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            message.To.Add(new MailAddress(toEmail.Trim()));

            await client.SendMailAsync(message);
            _logger.LogInformation("Successfully sent security email '{Subject}' to {Recipient}", subject, toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email '{Subject}' to {Recipient}: {Error}", subject, toEmail, ex.Message);
            return false;
        }
    }
}

