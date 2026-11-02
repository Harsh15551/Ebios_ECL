using System.Globalization;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class AttendanceService : IAttendanceService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AttendanceService> _logger;

    public AttendanceService(AppDbContext context, ILogger<AttendanceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<AttendanceLogDto>> GetLogsAsync(AttendanceFilterDto filter, int? userAreaId = null, int? userSubAreaId = null)
    {
        var query = _context.AttendanceLogs
            .AsNoTracking()
            .Include(l => l.Employee)
            .Include(l => l.Device)
            .Include(l => l.SubArea)
            .ThenInclude(s => s!.Area)
            .AsQueryable();

        // Role Scoping:
        if (userSubAreaId.HasValue)
        {
            query = query.Where(l => l.SubAreaId == userSubAreaId.Value);
        }
        else if (userAreaId.HasValue)
        {
            query = query.Where(l => l.SubArea != null && l.SubArea.AreaId == userAreaId.Value);
        }

        // Area / SubArea Filter
        if (filter.SubAreaId.HasValue && filter.SubAreaId.Value > 0)
        {
            query = query.Where(l => l.SubAreaId == filter.SubAreaId.Value);
        }
        else if (filter.AreaId.HasValue && filter.AreaId.Value > 0)
        {
            query = query.Where(l => l.SubArea != null && l.SubArea.AreaId == filter.AreaId.Value);
        }

        // Status Filter
        if (!string.IsNullOrWhiteSpace(filter.Status) && !filter.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => l.Status == filter.Status);
        }

        // EmpCode Filter
        if (!string.IsNullOrWhiteSpace(filter.EmpCode))
        {
            string code = filter.EmpCode.Trim().ToUpper();
            query = query.Where(l => l.EmpCode.ToUpper() == code);
        }

        // Date Range Filter
        if (filter.FromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(l => l.LogDate >= fromUtc);
        }
        if (filter.ToDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(filter.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(l => l.LogDate <= toUtc);
        }

        // Search Term (Name or Code)
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            string search = filter.SearchTerm.Trim().ToLower();
            query = query.Where(l => l.EmpCode.ToLower().Contains(search) ||
                                     (l.Employee != null && l.Employee.FullName.ToLower().Contains(search)) ||
                                     (l.DeviceName != null && l.DeviceName.ToLower().Contains(search)));
        }

        return await query
            .OrderByDescending(l => l.LogDate)
            .Take(1000)
            .Select(l => new AttendanceLogDto(
                l.Id,
                l.EmpCode,
                l.Employee != null ? l.Employee.FullName : (!string.IsNullOrWhiteSpace(l.RawEmployeeName) ? l.RawEmployeeName : "—"),
                l.EmployeeId,
                l.LogDate,
                l.DownloadDate,
                l.SerialNumber,
                l.DeviceName ?? (l.Device != null ? l.Device.DeviceName : "Terminal"),
                l.Direction,
                l.VerificationType,
                l.SubAreaId,
                l.SubArea != null ? l.SubArea.SubAreaName : "Unknown Unit",
                l.SubArea != null && l.SubArea.Area != null ? l.SubArea.Area.AreaName : "Unknown Area",
                l.Status,
                l.IsFlagged,
                l.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<List<AttendanceLogDto>> GetEmployeeLogsAsync(string empCode, DateTime? fromDate = null, DateTime? toDate = null)
    {
        string normalized = empCode.Trim().ToUpper();
        var query = _context.AttendanceLogs
            .AsNoTracking()
            .Include(l => l.Employee)
            .Include(l => l.Device)
            .Include(l => l.SubArea)
            .ThenInclude(s => s!.Area)
            .Where(l => l.EmpCode.ToUpper() == normalized);

        if (fromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(l => l.LogDate >= fromUtc);
        }
        if (toDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(l => l.LogDate <= toUtc);
        }

        return await query
            .OrderByDescending(l => l.LogDate)
            .Take(100)
            .Select(l => new AttendanceLogDto(
                l.Id,
                l.EmpCode,
                l.Employee != null ? l.Employee.FullName : (!string.IsNullOrWhiteSpace(l.RawEmployeeName) ? l.RawEmployeeName : "—"),
                l.EmployeeId,
                l.LogDate,
                l.DownloadDate,
                l.SerialNumber,
                l.DeviceName ?? (l.Device != null ? l.Device.DeviceName : "Terminal"),
                l.Direction,
                l.VerificationType,
                l.SubAreaId,
                l.SubArea != null ? l.SubArea.SubAreaName : "Unknown Unit",
                l.SubArea != null && l.SubArea.Area != null ? l.SubArea.Area.AreaName : "Unknown Area",
                l.Status,
                l.IsFlagged,
                l.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<int> ProcessWebhookPunchesAsync(List<WebhookPunchItem> punches)
    {
        if (punches == null || !punches.Any()) return 0;

        int processed = 0;
        foreach (var p in punches)
        {
            if (string.IsNullOrWhiteSpace(p.EmployeeCode) || string.IsNullOrWhiteSpace(p.SerialNumber))
            {
                continue;
            }

            string cleanEmpCode = p.EmployeeCode.Trim().ToUpperInvariant();
            string cleanSerial = p.SerialNumber.Trim().ToUpperInvariant();

            DateTime logDate = ParseDate(p.LogDate);
            DateTime? downloadDate = !string.IsNullOrWhiteSpace(p.DownloadDate) ? ParseDate(p.DownloadDate) : null;

            // Match device to find location & update health status
            var device = await _context.Devices
                .Include(d => d.SubArea)
                .FirstOrDefaultAsync(d => d.SerialNumber.ToUpper() == cleanSerial);

            if (device != null)
            {
                device.LastSyncedAt = DateTime.UtcNow;
                device.IsActive = true;
            }

            // Match employee
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmpCode.ToUpper() == cleanEmpCode);

            bool isKnownWorker = employee != null;

            var log = new AttendanceLog
            {
                EmpCode = cleanEmpCode,
                RawEmployeeName = p.ResolvedName,
                EmployeeId = employee?.Id,
                LogDate = logDate,
                DownloadDate = downloadDate,
                SerialNumber = cleanSerial,
                DeviceName = !string.IsNullOrWhiteSpace(p.DeviceName) ? p.DeviceName.Trim() : device?.DeviceName,
                DeviceId = device?.Id,
                Direction = (!string.IsNullOrWhiteSpace(p.Direction) ? p.Direction.Trim().ToUpperInvariant() : "IN"),
                DeviceDirection = p.DeviceDirection,
                VerificationType = p.VerificationType,
                WorkCode = p.WorkCode,
                Gps = p.GPS,
                SubAreaId = device?.SubAreaId ?? employee?.SubAreaId,
                Status = isKnownWorker ? "Verified" : "Flagged",
                IsFlagged = !isKnownWorker,
                CreatedAt = DateTime.UtcNow
            };

            _context.AttendanceLogs.Add(log);
            processed++;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Processed {Count} attendance punches via Webhook.", processed);
        return processed;
    }

    public async Task<bool> UpdateLogStatusAsync(int logId, string status)
    {
        var log = await _context.AttendanceLogs.FindAsync(logId);
        if (log == null) return false;

        log.Status = status;
        if (status == "Verified" || status == "Dismissed")
        {
            log.IsFlagged = false;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DismissFlaggedPunchesAsync(string empCode)
    {
        string norm = empCode.Trim().ToUpper();
        var logs = await _context.AttendanceLogs
            .Where(l => l.EmpCode.ToUpper() == norm && l.IsFlagged)
            .ToListAsync();

        if (!logs.Any()) return false;

        foreach (var l in logs)
        {
            l.Status = "Dismissed";
            l.IsFlagged = false;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Dismissed {Count} flagged punches for worker code {Code}.", logs.Count, empCode);
        return true;
    }

    public async Task<int> GetFlaggedCountAsync(int? areaId = null, int? subAreaId = null)
    {
        var query = _context.AttendanceLogs
            .AsNoTracking()
            .Where(l => l.IsFlagged);

        if (subAreaId.HasValue)
        {
            query = query.Where(l => l.SubAreaId == subAreaId.Value);
        }
        else if (areaId.HasValue)
        {
            query = query.Where(l => l.SubArea != null && l.SubArea.AreaId == areaId.Value);
        }

        return await query.CountAsync();
    }

    public async Task<bool> LinkPunchesToEmployeeAsync(string empCode, int employeeId)
    {
        string norm = empCode.Trim().ToUpper();
        var logs = await _context.AttendanceLogs
            .Where(l => l.EmpCode.ToUpper() == norm && (l.EmployeeId == null || l.IsFlagged))
            .ToListAsync();

        if (!logs.Any()) return false;

        foreach (var l in logs)
        {
            l.EmployeeId = employeeId;
            l.Status = "Verified";
            l.IsFlagged = false;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Retroactively linked {Count} punches for {Code} to Employee ID {Id}.", logs.Count, empCode, employeeId);
        return true;
    }

    private static DateTime ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return DateTime.UtcNow;

        string[] formats = {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "dd-MM-yyyy HH:mm:ss",
            "yyyy/MM/dd HH:mm:ss",
            "dd/MM/yyyy HH:mm:ss"
        };

        if (DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        if (DateTime.TryParse(dateStr.Trim(), out DateTime fallback))
        {
            return DateTime.SpecifyKind(fallback, DateTimeKind.Utc);
        }

        return DateTime.UtcNow;
    }
}

