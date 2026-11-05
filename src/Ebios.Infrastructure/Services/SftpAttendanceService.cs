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
    private readonly ILogger<SftpAttendanceService> _logger;

    public SftpAttendanceService(
        AppDbContext context,
        IConfiguration configuration,
        ILogger<SftpAttendanceService> logger)
    {
        _context = context;
        _configuration = configuration;
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

            string rficCard = EscapeCsv(s.Employee?.Id.ToString() ?? s.EmpCode);

            string machineId = !string.IsNullOrWhiteSpace(s.InSerialNumber) && s.InSerialNumber != "MANUAL"
                ? s.InSerialNumber
                : (!string.IsNullOrWhiteSpace(s.InDeviceName) ? s.InDeviceName : "abc");
            machineId = EscapeCsv(machineId);

            sb.AppendLine($"{empId},{attDate},{inDate},{inTime},{outDate},{outTime},{rficCard},{machineId}");
        }

        return sb.ToString();
    }

    public async Task<SftpExportResultDto> ExportAndUploadDailyAttendanceAsync(DateTime attendanceDate, int? areaId = null, int? subAreaId = null)
    {
        var (host, port, username, password, remotePath) = GetSftpConfig();

        try
        {
            string csvContent = await GenerateAttendanceCsvAsync(attendanceDate, areaId, subAreaId);
            int recordCount = Math.Max(0, csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);

            string fileName = $"ATTENDANCE_{attendanceDate:yyyyMMdd}_{DateTime.Now:HHmmss}.csv";
            string targetPath = remotePath.TrimEnd('/') + "/" + fileName;

            var connInfo = new ConnectionInfo(host, port, username,
                new PasswordAuthenticationMethod(username, password))
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            using var client = new SftpClient(connInfo);
            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
            {
                return new SftpExportResultDto(false, $"Could not connect to SFTP server at {host}:{port}");
            }

            // Ensure destination directory exists if nested
            string dir = Path.GetDirectoryName(targetPath)?.Replace('\\', '/') ?? "/";
            if (!string.IsNullOrWhiteSpace(dir) && dir != "/" && !client.Exists(dir))
            {
                CreateDirectoryRecursively(client, dir);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(csvContent);
            using (var stream = new MemoryStream(bytes))
            {
                await Task.Run(() => client.UploadFile(stream, targetPath, true));
            }

            client.Disconnect();

            _logger.LogInformation("Successfully uploaded {Count} attendance records to SFTP {Host}:{Port}{Path}", 
                recordCount, host, port, targetPath);

            return new SftpExportResultDto(
                Success: true,
                Message: $"Successfully transmitted {recordCount} records to SFTP ({fileName})",
                RemoteFilePath: targetPath,
                RecordCount: recordCount,
                TransmittedAt: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export and upload attendance via SFTP to {Host}:{Port}", host, port);
            return new SftpExportResultDto(
                Success: false,
                Message: $"SFTP Transfer Error: {ex.Message}",
                TransmittedAt: DateTime.UtcNow
            );
        }
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

