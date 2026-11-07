using System.Text;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Ebios.Infrastructure.Services;

public class SftpAttendanceService : ISftpAttendanceService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _auditService;
    private readonly ILogger<SftpAttendanceService> _logger;
    private static string? _configuredScheduleTime;

    public SftpAttendanceService(
        AppDbContext context,
        IConfiguration configuration,
        IAuditService auditService,
        ILogger<SftpAttendanceService> logger)
    {
        _context = context;
        _configuration = configuration;
        _auditService = auditService;
        _logger = logger;
    }

    private (string host, int port, string username, string password, string remotePath) GetSftpConfig()
    {
        string host = Environment.GetEnvironmentVariable("SFTP_HOST")
                      ?? _configuration["SftpServer:Host"]
                      ?? "127.0.0.1";

        string portStr = Environment.GetEnvironmentVariable("SFTP_PORT")
                         ?? _configuration["SftpServer:Port"]
                         ?? "22";
        int.TryParse(portStr, out int port);
        if (port <= 0) port = 22;

        string username = Environment.GetEnvironmentVariable("SFTP_USERNAME")
                          ?? _configuration["SftpServer:UserName"]
                          ?? "tester";

        string password = Environment.GetEnvironmentVariable("SFTP_PASSWORD")
                          ?? _configuration["SftpServer:Password"]
                          ?? "password";

        string remotePath = Environment.GetEnvironmentVariable("SFTP_REMOTE_PATH")
                            ?? _configuration["SftpServer:RemotePath"]
                            ?? "/";

        return (host, port, username, password, remotePath);
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        var (host, port, username, password, remotePath) = GetSftpConfig();

        try
        {
            var connInfo = new ConnectionInfo(host, port, username,
                new PasswordAuthenticationMethod(username, password))
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            using var client = new SftpClient(connInfo);
            await Task.Run(() => client.Connect());

            bool isConnected = client.IsConnected;
            if (isConnected)
            {
                string workingDir = client.WorkingDirectory;
                client.Disconnect();
                return (true, $"Connected successfully to SFTP server at {host}:{port} (Working Directory: '{workingDir}')");
            }

            return (false, $"Unable to establish connection to {host}:{port}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SFTP connection test failed: {Message}", ex.Message);
            return (false, $"SFTP Connection Failed: {ex.Message}");
        }
    }

    public string GetDailyScheduleTime()
    {
        return _configuredScheduleTime
               ?? Environment.GetEnvironmentVariable("SFTP_SCHEDULE_TIME")
               ?? _configuration["SftpServer:DailyScheduledTime"]
               ?? "23:45";
    }

    public async Task<(bool Success, string Message)> SetDailyScheduleTimeAsync(
        string newScheduleTime,
        int? userId = null,
        string username = "System",
        string officerName = "System Worker",
        string role = "System")
    {
        if (string.IsNullOrWhiteSpace(newScheduleTime))
        {
            return (false, "Schedule time cannot be blank.");
        }

        if (!TimeSpan.TryParse(newScheduleTime.Trim(), out var parsedTime) || parsedTime.TotalHours < 0 || parsedTime.TotalHours >= 24)
        {
            return (false, "Invalid time format. Please provide time in HH:mm format (e.g. 23:45).");
        }

        string formattedTime = new DateTime(2000, 1, 1, parsedTime.Hours, parsedTime.Minutes, 0).ToString("HH:mm");
        string previousTime = GetDailyScheduleTime();

        _configuredScheduleTime = formattedTime;
        Environment.SetEnvironmentVariable("SFTP_SCHEDULE_TIME", formattedTime);

        // Also update .env file if available for permanence
        try
        {
            string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (!File.Exists(envPath))
            {
                envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            }

            if (File.Exists(envPath))
            {
                var lines = (await File.ReadAllLinesAsync(envPath)).ToList();
                int idx = lines.FindIndex(l => l.TrimStart().StartsWith("SFTP_SCHEDULE_TIME="));
                if (idx >= 0)
                {
                    lines[idx] = $"SFTP_SCHEDULE_TIME={formattedTime}";
                }
                else
                {
                    lines.Add($"SFTP_SCHEDULE_TIME={formattedTime}");
                }
                await File.WriteAllLinesAsync(envPath, lines);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist SFTP_SCHEDULE_TIME to .env file: {Message}", ex.Message);
        }

        await _auditService.LogSecurityActionAsync(
            "SFTP_SCHEDULE_TIME_UPDATED",
            "SFTP_SCHEDULE",
            $"Automated daily SFTP attendance sync time changed from {previousTime} to {formattedTime} by {username}.",
            userId,
            username,
            officerName,
            role);

        _logger.LogInformation("SFTP Daily Scheduled Time updated to {Time} by {User}", formattedTime, username);
        return (true, $"Automated SFTP schedule timing successfully updated to {formattedTime}.");
    }

    public async Task<string> GenerateAttendanceCsvAsync(DateTime attendanceDate, int? areaId = null, int? subAreaId = null)
    {
        DateTime targetDate = DateTime.SpecifyKind(attendanceDate.Date, DateTimeKind.Utc);

        var query = _context.AttendanceDailySummaries
            .Include(s => s.SubArea)
            .Include(s => s.Employee)
            .Where(s => s.AttendanceDate == targetDate);

        if (subAreaId.HasValue && subAreaId.Value > 0)
        {
            query = query.Where(s => s.SubAreaId == subAreaId.Value);
        }
        else if (areaId.HasValue && areaId.Value > 0)
        {
            query = query.Where(s => s.SubArea != null && s.SubArea.AreaId == areaId.Value);
        }

        var summaries = await query
            .OrderBy(s => s.EmpCode)
            .ToListAsync();

        var sb = new StringBuilder();
        // Exact header as requested
        sb.AppendLine("Employee Id,Attendance date,Capture_date in,Employee time in,Capture_date out,Employee time out,RFIC_card number,Biometric machine id");

        foreach (var s in summaries)
        {
            string empId = EscapeCsv(s.EmpCode);
            string attDate = s.AttendanceDate.ToString("dd.MM.yyyy");

            string inDate = s.FirstInTime.HasValue ? s.FirstInTime.Value.ToString("dd.MM.yyyy") : "";
            string inTime = s.FirstInTime.HasValue ? s.FirstInTime.Value.ToString("H:mm:ss") : "";

            string outDate = s.LastOutTime.HasValue ? s.LastOutTime.Value.ToString("dd.MM.yyyy") : "";
            string outTime = s.LastOutTime.HasValue ? s.LastOutTime.Value.ToString("H:mm:ss") : "";

            // Requirement 1: RFIC in SFTP report marked as blank/empty because raw logs do not supply RFID/RFIC yet
            string rficCard = "";

            string machineId = !string.IsNullOrWhiteSpace(s.InSerialNumber) && s.InSerialNumber != "MANUAL"
                ? s.InSerialNumber
                : (!string.IsNullOrWhiteSpace(s.InDeviceName) ? s.InDeviceName : "abc");
            machineId = EscapeCsv(machineId);

            sb.AppendLine($"{empId},{attDate},{inDate},{inTime},{outDate},{outTime},{rficCard},{machineId}");
        }

        return sb.ToString();
    }

    public async Task<SftpExportResultDto> ExportAndUploadDailyAttendanceAsync(
        DateTime attendanceDate,
        int? areaId = null,
        int? subAreaId = null,
        string triggerType = "MANUAL",
        int? userId = null,
        string username = "System",
        string officerName = "System Worker",
        string role = "System")
    {
        var (host, port, sftpUser, password, remotePath) = GetSftpConfig();
        string fileName = $"ATTENDANCE_{attendanceDate:yyyyMMdd}_{DateTime.Now:HHmmss}.csv";
        string targetPath = remotePath.TrimEnd('/') + "/" + fileName;

        string csvContent;
        int recordCount = 0;
        try
        {
            csvContent = await GenerateAttendanceCsvAsync(attendanceDate, areaId, subAreaId);
            recordCount = Math.Max(0, csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        }
        catch (Exception ex)
        {
            await _auditService.LogSftpTransmissionAsync(
                fileName, attendanceDate, 0, "FAILED", $"CSV Generation Error: {ex.Message}",
                null, host, port, targetPath, triggerType, userId, username, officerName, role);
            return new SftpExportResultDto(false, $"Failed to generate attendance CSV: {ex.Message}");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(csvContent);
        int maxRetries = 3;
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var connInfo = new ConnectionInfo(host, port, sftpUser,
                    new PasswordAuthenticationMethod(sftpUser, password))
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

                using var client = new SftpClient(connInfo);
                await Task.Run(() => client.Connect());

                if (!client.IsConnected)
                {
                    throw new InvalidOperationException($"Could not establish connection to {host}:{port}");
                }

                // Ensure destination directory exists if nested
                string dir = Path.GetDirectoryName(targetPath)?.Replace('\\', '/') ?? "/";
                if (!string.IsNullOrWhiteSpace(dir) && dir != "/" && !client.Exists(dir))
                {
                    CreateDirectoryRecursively(client, dir);
                }

                // Upload
                using (var stream = new MemoryStream(bytes))
                {
                    await Task.Run(() => client.UploadFile(stream, targetPath, true));
                }

                // Remote Verification: Check if remote server actually holds the file and verify byte size!
                long remoteSize = 0;
                bool verified = false;
                if (client.Exists(targetPath))
                {
                    var remoteAttr = client.Get(targetPath);
                    remoteSize = remoteAttr.Length;
                    verified = (remoteSize == bytes.Length);
                }

                client.Disconnect();

                if (!verified)
                {
                    throw new InvalidOperationException($"Remote delivery verification failed: expected {bytes.Length} bytes, remote holds {remoteSize} bytes.");
                }

                _logger.LogInformation("Successfully uploaded and verified {Count} attendance records ({Bytes} bytes) to SFTP {Host}:{Port}{Path}",
                    recordCount, remoteSize, host, port, targetPath);

                // Audit Log Success
                await _auditService.LogSftpTransmissionAsync(
                    fileName, attendanceDate, recordCount, "DELIVERED_VERIFIED", null,
                    remoteSize, host, port, targetPath, triggerType, userId, username, officerName, role);

                return new SftpExportResultDto(
                    Success: true,
                    Message: $"Delivered & Verified {recordCount} records on SFTP server ({fileName})",
                    RemoteFilePath: targetPath,
                    RecordCount: recordCount,
                    TransmittedAt: DateTime.UtcNow
                );
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _logger.LogWarning("SFTP transmission attempt {Attempt}/{Max} failed: {Error}", attempt, maxRetries, ex.Message);
                if (attempt < maxRetries)
                {
                    await Task.Delay(2000 * attempt); // 2s, 4s delay between retries
                }
            }
        }

        // Audit Log Failure
        string failureMsg = lastEx?.Message ?? "Unknown SFTP failure";
        await _auditService.LogSftpTransmissionAsync(
            fileName, attendanceDate, recordCount, "FAILED", failureMsg,
            null, host, port, targetPath, triggerType, userId, username, officerName, role);

        return new SftpExportResultDto(
            Success: false,
            Message: $"SFTP Transfer Failed after {maxRetries} attempts: {failureMsg}",
            TransmittedAt: DateTime.UtcNow
        );
    }

    private static void CreateDirectoryRecursively(SftpClient client, string path)
    {
        string current = "";
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            current += "/" + part;
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}

