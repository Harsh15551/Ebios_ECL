using System.Text.Json;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext context, ILogger<AuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(AuditLog entry)
    {
        try
        {
            entry.Timestamp = DateTime.UtcNow;
            _context.AuditLogs.Add(entry);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist audit log entry [{Category} - {Action}]: {Error}", 
                entry.Category, entry.Action, ex.Message);
        }
    }

    public async Task LogRegularizationAsync(
        string empCode,
        DateTime attendanceDate,
        string? oldStateJson,
        string newStateJson,
        bool isOverwrite,
        string details,
        int? userId,
        string username,
        string officerName,
        string role)
    {
        var entry = new AuditLog
        {
            Category = "Regularization",
            Action = isOverwrite ? "REGULARIZATION_OVERWRITTEN" : "REGULARIZATION_APPLIED",
            EntityReference = $"EMP:{empCode.ToUpper()} | Date:{attendanceDate:yyyy-MM-dd}",
            PreviousStateJson = oldStateJson,
            NewStateJson = newStateJson,
            Details = details,
            Status = "SUCCESS",
            UserId = userId,
            Username = username,
            OfficerName = officerName,
            Role = role,
            Timestamp = DateTime.UtcNow
        };

        await LogAsync(entry);
    }

    public async Task LogSftpTransmissionAsync(
        string fileName,
        DateTime targetDate,
        int recordCount,
        string status,
        string? error,
        long? remoteSizeBytes,
        string remoteHost,
        int remotePort,
        string remotePath,
        string triggerType,
        int? userId,
        string username,
        string officerName,
        string role)
    {
        var payload = new
        {
            targetDate = targetDate.ToString("yyyy-MM-dd"),
            recordCount,
            fileSizeBytes = remoteSizeBytes,
            remoteHost,
            remotePort,
            remotePath,
            status,
            error,
            triggerType,
            completedAt = DateTime.UtcNow
        };

        string json = JsonSerializer.Serialize(payload);

        bool isSuccess = status == "DELIVERED_VERIFIED" || status == "SUCCESS";
        string details = isSuccess
            ? $"Uploaded {recordCount} records ({remoteSizeBytes ?? 0:N0} bytes) to {remoteHost}:{remotePort}{remotePath}. Remote file delivery verified."
            : $"SFTP upload failed for {fileName}: {error}";

        var entry = new AuditLog
        {
            Category = "SftpDispatch",
            Action = isSuccess ? "SFTP_DELIVERED_VERIFIED" : "SFTP_TRANSMISSION_FAILED",
            EntityReference = fileName,
            PreviousStateJson = null,
            NewStateJson = json,
            Details = details,
            Status = isSuccess ? "SUCCESS" : "FAILED",
            UserId = userId,
            Username = username,
            OfficerName = officerName,
            Role = role,
            Timestamp = DateTime.UtcNow
        };

        await LogAsync(entry);
    }

    public async Task LogSecurityActionAsync(
        string action,
        string reference,
        string details,
        int? userId,
        string username,
        string officerName,
        string role,
        string? oldJson = null,
        string? newJson = null)
    {
        var entry = new AuditLog
        {
            Category = "Security",
            Action = action,
            EntityReference = reference,
            PreviousStateJson = oldJson,
            NewStateJson = newJson,
            Details = details,
            Status = "SUCCESS",
            UserId = userId,
            Username = username,
            OfficerName = officerName,
            Role = role,
            Timestamp = DateTime.UtcNow
        };

        await LogAsync(entry);
    }

    public async Task<LastSftpStatusDto> GetLastSftpStatusAsync()
    {
        var last = await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.Category == "SftpDispatch")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();

        if (last == null)
        {
            return new LastSftpStatusDto(
                HasRun: false,
                Timestamp: null,
                Status: "NONE",
                FileName: null,
                RecordCount: 0,
                FileSizeBytes: null,
                RemoteHost: null,
                RemotePath: null,
                ErrorMessage: null,
                TriggerType: null,
                OfficerName: null
            );
        }

        int records = 0;
        long? size = null;
        string? host = null;
        string? path = null;
        string? err = null;
        string? trigger = null;

        if (!string.IsNullOrWhiteSpace(last.NewStateJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(last.NewStateJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("recordCount", out var rc)) records = rc.GetInt32();
                if (root.TryGetProperty("fileSizeBytes", out var sb) && sb.ValueKind == JsonValueKind.Number) size = sb.GetInt64();
                if (root.TryGetProperty("remoteHost", out var rh)) host = rh.GetString();
                if (root.TryGetProperty("remotePath", out var rp)) path = rp.GetString();
                if (root.TryGetProperty("error", out var re)) err = re.GetString();
                if (root.TryGetProperty("triggerType", out var rt)) trigger = rt.GetString();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse NewStateJson for SFTP audit log #{LogId}. Falling back to default text properties.", last.Id);
            }
        }

        return new LastSftpStatusDto(
            HasRun: true,
            Timestamp: last.Timestamp,
            Status: last.Status,
            FileName: last.EntityReference,
            RecordCount: records,
            FileSizeBytes: size,
            RemoteHost: host,
            RemotePath: path,
            ErrorMessage: err,
            TriggerType: trigger ?? "MANUAL",
            OfficerName: last.OfficerName
        );
    }

    public async Task<List<AuditLogDto>> GetRecentSftpLogsAsync(int count = 10)
    {
        return await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.Category == "SftpDispatch")
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .Select(a => new AuditLogDto(
                a.Id,
                a.Category,
                a.Action,
                a.EntityReference,
                a.PreviousStateJson,
                a.NewStateJson,
                a.Details,
                a.Status,
                a.UserId,
                a.Username,
                a.OfficerName,
                a.Role,
                a.IpAddress,
                a.Timestamp
            ))
            .ToListAsync();
    }

    public async Task<List<AuditLogDto>> GetRecordAuditHistoryAsync(string entityReference)
    {
        return await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityReference == entityReference)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new AuditLogDto(
                a.Id,
                a.Category,
                a.Action,
                a.EntityReference,
                a.PreviousStateJson,
                a.NewStateJson,
                a.Details,
                a.Status,
                a.UserId,
                a.Username,
                a.OfficerName,
                a.Role,
                a.IpAddress,
                a.Timestamp
            ))
            .ToListAsync();
    }

    public async Task<AuditPagedResultDto> GetAuditLogsPagedAsync(AuditFilterDto filter)
    {
        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Category) && filter.Category != "ALL")
        {
            query = query.Where(a => a.Category == filter.Category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "ALL")
        {
            query = query.Where(a => a.Status == filter.Status);
        }

        if (filter.FromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp >= fromUtc);
        }

        if (filter.ToDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(filter.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            string term = filter.SearchText.Trim().ToLower();
            query = query.Where(a =>
                a.EntityReference.ToLower().Contains(term) ||
                a.Details.ToLower().Contains(term) ||
                a.OfficerName.ToLower().Contains(term) ||
                a.Username.ToLower().Contains(term));
        }

        int totalCount = await query.CountAsync();

        int page = Math.Max(1, filter.Page);
        int pageSize = Math.Clamp(filter.PageSize, 10, 100);

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
                a.Id,
                a.Category,
                a.Action,
                a.EntityReference,
                a.PreviousStateJson,
                a.NewStateJson,
                a.Details,
                a.Status,
                a.UserId,
                a.Username,
                a.OfficerName,
                a.Role,
                a.IpAddress,
                a.Timestamp
            ))
            .ToListAsync();

        return new AuditPagedResultDto(items, totalCount, page, pageSize);
    }
}

