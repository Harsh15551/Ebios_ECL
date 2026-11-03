using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class AttendanceProcessingService : IAttendanceProcessingService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AttendanceProcessingService> _logger;

    public AttendanceProcessingService(AppDbContext context, ILogger<AttendanceProcessingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ProcessPunchIntoMusterRollAsync(AttendanceLog punch)
    {
        if (punch == null || string.IsNullOrWhiteSpace(punch.EmpCode)) return;

        string cleanCode = punch.EmpCode.Trim().ToUpper();
        DateTime punchTime = punch.LogDate;
        TimeSpan punchTimeOfDay = punchTime.TimeOfDay;
        DateTime punchDate = DateTime.SpecifyKind(punchTime.Date, DateTimeKind.Utc);

        var shifts = await _context.ShiftMasters.AsNoTracking().Where(s => s.IsActive).ToListAsync();

        // 1. Check for Open Night Shift from Yesterday (22:00 to 06:00 / 21:00 to 05:00)
        DateTime yesterdayDate = punchDate.AddDays(-1);
        var yesterdayNightSummary = await _context.AttendanceDailySummaries
            .Include(s => s.Shift)
            .FirstOrDefaultAsync(s => s.EmpCode.ToUpper() == cleanCode && 
                                      s.AttendanceDate == yesterdayDate && 
                                      s.Shift != null && s.Shift.IsNightShift && 
                                      s.LastOutTime == null);

        if (yesterdayNightSummary != null && yesterdayNightSummary.Shift != null)
        {
            var nightShift = yesterdayNightSummary.Shift;
            // Does this punch fall in the night shift OUT window (e.g. 05:00 to 09:00)?
            bool isInNightOutWindow = (punchTimeOfDay >= nightShift.OutWindowStart && punchTimeOfDay <= nightShift.OutWindowEnd) ||
                                      (punchTime > yesterdayNightSummary.FirstInTime?.AddHours(4)); // Fallback: At least 4 hrs after entry

            if (isInNightOutWindow)
            {
                // This is the EXIT of yesterday's night shift!
                yesterdayNightSummary.LastOutTime = punchTime;
                yesterdayNightSummary.OutSerialNumber = punch.SerialNumber;
                yesterdayNightSummary.OutDeviceName = punch.DeviceName;
                CalculateSummaryMetrics(yesterdayNightSummary, nightShift);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Matched exit punch for night shift on {Date} for {Code} at {Machine}.", yesterdayDate, cleanCode, punch.DeviceName);
                return;
            }
        }

        // 2. Check for Existing Summary on Today's Date
        var todaySummary = await _context.AttendanceDailySummaries
            .Include(s => s.Shift)
            .FirstOrDefaultAsync(s => s.EmpCode.ToUpper() == cleanCode && s.AttendanceDate == punchDate);

        var existingEmp = punch.Employee ?? await _context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode.ToUpper() == cleanCode);
        bool isEmployeeKnown = existingEmp != null;
        int? resolvedEmpId = existingEmp?.Id ?? punch.EmployeeId;
        string empDisplayName = existingEmp?.FullName 
                             ?? (!string.IsNullOrWhiteSpace(punch.RawEmployeeName) ? punch.RawEmployeeName : $"Unregistered ({cleanCode})");

        if (todaySummary != null)
        {
            var shift = todaySummary.Shift ?? shifts.FirstOrDefault(s => s.ShiftCode == todaySummary.ShiftCode);

            // Update employee metadata if newly identified
            if (isEmployeeKnown && (!todaySummary.IsVerified || todaySummary.EmployeeId == null))
            {
                todaySummary.EmployeeId = resolvedEmpId;
                todaySummary.EmployeeName = empDisplayName;
                todaySummary.IsVerified = true;
                todaySummary.IsFlagged = false;
                if (todaySummary.Remarks != null && todaySummary.Remarks.Contains("Flagged"))
                {
                    todaySummary.Remarks = todaySummary.RosterStatus == "Auto-Matched" ? $"Auto-matched to {todaySummary.ShiftCode}" : null;
                }
            }

            if (todaySummary.SubAreaId == null && punch.SubAreaId.HasValue)
            {
                todaySummary.SubAreaId = punch.SubAreaId.Value;
            }

            // If FirstInTime is set and this punch is later, treat as OUT (or update latest OUT)
            if (todaySummary.FirstInTime.HasValue && punchTime > todaySummary.FirstInTime.Value)
            {
                todaySummary.LastOutTime = punchTime;
                todaySummary.OutSerialNumber = punch.SerialNumber;
                todaySummary.OutDeviceName = punch.DeviceName;

                if (shift != null)
                {
                    CalculateSummaryMetrics(todaySummary, shift);
                }
            }
            else if (!todaySummary.FirstInTime.HasValue || punchTime < todaySummary.FirstInTime.Value)
            {
                // Earlier punch becomes FirstInTime
                todaySummary.FirstInTime = punchTime;
                todaySummary.InSerialNumber = punch.SerialNumber;
                todaySummary.InDeviceName = punch.DeviceName;

                if (shift != null)
                {
                    CalculateSummaryMetrics(todaySummary, shift);
                }
            }

            todaySummary.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return;
        }

        // 3. Brand New Attendance Day - Resolve Shift
        // Step A: Check assigned roster
        var roster = await _context.EmployeeShiftRosters
            .Include(r => r.Shift)
            .FirstOrDefaultAsync(r => r.EmpCode.ToUpper() == cleanCode && r.RosterDate == punchDate);

        ShiftMaster? resolvedShift = roster?.Shift;
        string rosterStatus = "Assigned";

        // Step B: If no roster assigned, auto-detect from Shift Windows
        if (resolvedShift == null)
        {
            rosterStatus = "Auto-Matched";
            // Check if punch falls in InWindow
            resolvedShift = shifts.FirstOrDefault(s => punchTimeOfDay >= s.InWindowStart && punchTimeOfDay <= s.InWindowEnd);

            if (resolvedShift == null)
            {
                // Fallback to closest start time
                resolvedShift = shifts
                    .OrderBy(s => Math.Abs((s.StartTime - punchTimeOfDay).TotalMinutes))
                    .FirstOrDefault();
            }
        }

        resolvedShift ??= shifts.FirstOrDefault(s => s.ShiftCode == "General") ?? shifts.FirstOrDefault();

        // 4. Assemble New Summary Record
        var newSummary = new AttendanceDailySummary
        {
            AttendanceDate = punchDate,
            EmpCode = cleanCode,
            EmployeeName = empDisplayName,
            EmployeeId = resolvedEmpId,
            SubAreaId = punch.SubAreaId ?? existingEmp?.SubAreaId,
            ShiftId = resolvedShift?.Id,
            ShiftCode = resolvedShift?.ShiftCode ?? "General",
            FirstInTime = punchTime,
            InSerialNumber = punch.SerialNumber,
            InDeviceName = punch.DeviceName,
            Status = "Single Punch",
            RosterStatus = rosterStatus,
            IsVerified = isEmployeeKnown,
            IsFlagged = !isEmployeeKnown,
            Remarks = !isEmployeeKnown ? "Flagged: Unregistered Worker" : (rosterStatus == "Auto-Matched" ? $"Auto-matched to {resolvedShift?.ShiftCode}" : null),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (resolvedShift != null)
        {
            // Evaluate Late Entry
            TimeSpan permissibleLate = resolvedShift.StartTime.Add(TimeSpan.FromMinutes(resolvedShift.GraceMinutes));
            if (punchTimeOfDay > permissibleLate)
            {
                newSummary.LateByMinutes = (int)(punchTimeOfDay - resolvedShift.StartTime).TotalMinutes;
                newSummary.Status = "Single Punch (Late)";
            }
        }

        _context.AttendanceDailySummaries.Add(newSummary);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Created new daily attendance summary for {Code} on {Date} [{Shift}] via {Machine}.", cleanCode, punchDate.ToShortDateString(), resolvedShift?.ShiftCode, punch.DeviceName);
    }

    private static void CalculateSummaryMetrics(AttendanceDailySummary summary, ShiftMaster shift)
    {
        if (!summary.FirstInTime.HasValue || !summary.LastOutTime.HasValue) return;

        int totalMinutes = (int)(summary.LastOutTime.Value - summary.FirstInTime.Value).TotalMinutes;
        if (totalMinutes < 0) totalMinutes = 0;

        summary.TotalWorkMinutes = totalMinutes;
        summary.TotalWorkHours = Math.Round(totalMinutes / 60.0, 2);

        // Evaluate Early Entry / Late Entry
        TimeSpan inTimeOfDay = summary.FirstInTime.Value.TimeOfDay;
        TimeSpan permissibleLate = shift.StartTime.Add(TimeSpan.FromMinutes(shift.GraceMinutes));
        if (inTimeOfDay > permissibleLate)
        {
            summary.LateByMinutes = (int)(inTimeOfDay - shift.StartTime).TotalMinutes;
        }
        else
        {
            summary.LateByMinutes = 0;
        }

        // Evaluate Early Exit (No Grace Period on Exit)
        if (!shift.IsNightShift)
        {
            TimeSpan outTimeOfDay = summary.LastOutTime.Value.TimeOfDay;
            if (outTimeOfDay < shift.EndTime)
            {
                summary.EarlyExitMinutes = (int)(shift.EndTime - outTimeOfDay).TotalMinutes;
            }
            else
            {
                summary.EarlyExitMinutes = 0;
            }
        }
        else
        {
            // Night Shift: End time is next morning
            TimeSpan outTimeOfDay = summary.LastOutTime.Value.TimeOfDay;
            if (outTimeOfDay < shift.EndTime)
            {
                summary.EarlyExitMinutes = (int)(shift.EndTime - outTimeOfDay).TotalMinutes;
            }
            else
            {
                summary.EarlyExitMinutes = 0;
            }
        }

        // Evaluate Overtime
        int standardShiftMinutes = (int)(shift.DurationHours * 60);
        if (totalMinutes > standardShiftMinutes)
        {
            summary.OvertimeMinutes = totalMinutes - standardShiftMinutes;
        }
        else
        {
            summary.OvertimeMinutes = 0;
        }

        // Status Determination:
        if (summary.TotalWorkHours >= 14.0)
        {
            summary.Status = "Double Shift";
            summary.Remarks = $"Double Shift: {summary.TotalWorkHours} hrs (OT: {Math.Round(summary.OvertimeMinutes / 60.0, 1)} hrs)";
        }
        else if (summary.TotalWorkHours >= 7.5)
        {
            summary.Status = summary.LateByMinutes > 0 ? "Present (Late)" : "Present";
        }
        else if (summary.TotalWorkHours >= 4.0)
        {
            summary.Status = "Half Day";
        }
        else
        {
            summary.Status = "Short Hours";
        }
    }

    public async Task<int> RecalculateDailyMusterRollAsync(DateTime fromDate, DateTime toDate, int? subAreaId = null)
    {
        var fromUtc = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(toDate.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

        // Remove existing daily summaries for this date range so they are freshly calculated from raw punches
        var existingSummariesQuery = _context.AttendanceDailySummaries
            .Where(s => s.AttendanceDate >= fromUtc && s.AttendanceDate <= toUtc);

        if (subAreaId.HasValue && subAreaId.Value > 0)
        {
            existingSummariesQuery = existingSummariesQuery.Where(s => s.SubAreaId == subAreaId.Value);
        }

        var existingSummaries = await existingSummariesQuery.ToListAsync();
        if (existingSummaries.Any())
        {
            _context.AttendanceDailySummaries.RemoveRange(existingSummaries);
            await _context.SaveChangesAsync();
        }

        // Fetch all raw punches (both verified and flagged) for this date range
        var query = _context.AttendanceLogs
            .Include(l => l.Employee)
            .Include(l => l.Device)
            .Where(l => l.LogDate >= fromUtc && l.LogDate <= toUtc);

        if (subAreaId.HasValue && subAreaId.Value > 0)
        {
            query = query.Where(l => l.SubAreaId == subAreaId.Value);
        }

        var punches = await query.OrderBy(l => l.LogDate).ToListAsync();
        int count = 0;

        foreach (var p in punches)
        {
            await ProcessPunchIntoMusterRollAsync(p);
            count++;
        }

        return count;
    }

    public async Task<List<DailyMusterRollDto>> GetMusterRollReportAsync(MusterRollFilterDto filter, int? userAreaId = null, int? userSubAreaId = null)
    {
        DateTime fromUtc = filter.FromDate.HasValue
            ? DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc)
            : (filter.Date.HasValue 
                ? DateTime.SpecifyKind(filter.Date.Value.Date, DateTimeKind.Utc) 
                : DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc));

        DateTime toUtc = filter.ToDate.HasValue
            ? DateTime.SpecifyKind(filter.ToDate.Value.Date, DateTimeKind.Utc)
            : (filter.Date.HasValue 
                ? DateTime.SpecifyKind(filter.Date.Value.Date, DateTimeKind.Utc) 
                : DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc));

        var query = _context.AttendanceDailySummaries
            .AsNoTracking()
            .Include(s => s.Employee)
            .Include(s => s.SubArea)
            .ThenInclude(sub => sub!.Area)
            .Include(s => s.Shift)
            .Where(s => s.AttendanceDate >= fromUtc && s.AttendanceDate <= toUtc);

        // Role scoping:
        if (userSubAreaId.HasValue && userSubAreaId.Value > 0)
        {
            query = query.Where(s => s.SubAreaId == userSubAreaId.Value);
        }
        else if (userAreaId.HasValue && userAreaId.Value > 0)
        {
            query = query.Where(s => s.SubArea != null && s.SubArea.AreaId == userAreaId.Value);
        }

        // SubArea filter:
        if (filter.SubAreaId.HasValue && filter.SubAreaId.Value > 0)
        {
            query = query.Where(s => s.SubAreaId == filter.SubAreaId.Value);
        }
        else if (filter.AreaId.HasValue && filter.AreaId.Value > 0)
        {
            query = query.Where(s => s.SubArea != null && s.SubArea.AreaId == filter.AreaId.Value);
        }

        // Shift filter:
        if (!string.IsNullOrWhiteSpace(filter.ShiftCode) && !filter.ShiftCode.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(s => s.ShiftCode == filter.ShiftCode);
        }

        // Status filter:
        if (!string.IsNullOrWhiteSpace(filter.Status) && !filter.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(s => s.Status.Contains(filter.Status));
        }

        // Search term:
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            string term = filter.SearchTerm.Trim().ToLower();
            query = query.Where(s => s.EmpCode.ToLower().Contains(term) || 
                                     (s.EmployeeName != null && s.EmployeeName.ToLower().Contains(term)) ||
                                     (s.InDeviceName != null && s.InDeviceName.ToLower().Contains(term)) ||
                                     (s.OutDeviceName != null && s.OutDeviceName.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(s => s.AttendanceDate)
            .ThenBy(s => s.EmpCode)
            .Select(s => new DailyMusterRollDto(
                s.Id,
                s.AttendanceDate,
                s.EmpCode,
                s.Employee != null ? s.Employee.FullName : s.EmployeeName,
                s.EmployeeId,
                s.SubAreaId,
                s.SubArea != null ? s.SubArea.SubAreaName : "—",
                s.SubArea != null && s.SubArea.Area != null ? s.SubArea.Area.AreaName : "—",
                s.ShiftCode,
                s.Shift != null ? s.Shift.ShiftName : s.ShiftCode,
                s.FirstInTime,
                s.InSerialNumber,
                s.InDeviceName,
                s.LastOutTime,
                s.OutSerialNumber,
                s.OutDeviceName,
                s.TotalWorkMinutes,
                s.TotalWorkHours,
                s.OvertimeMinutes,
                s.LateByMinutes,
                s.EarlyExitMinutes,
                s.Status,
                s.RosterStatus,
                s.Remarks,
                s.IsVerified,
                s.IsFlagged,
                s.CreatedAt,
                s.UpdatedAt
            ))
            .ToListAsync();
    }

    public async Task<MusterRollKpiDto> GetMusterRollKpisAsync(DateTime fromDate, DateTime toDate, int? userAreaId = null, int? userSubAreaId = null)
    {
        DateTime fromUtc = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Utc);
        DateTime toUtc = DateTime.SpecifyKind(toDate.Date, DateTimeKind.Utc);

        var query = _context.AttendanceDailySummaries
            .AsNoTracking()
            .Include(s => s.SubArea)
            .Where(s => s.AttendanceDate >= fromUtc && s.AttendanceDate <= toUtc);

        if (userSubAreaId.HasValue && userSubAreaId.Value > 0)
        {
            query = query.Where(s => s.SubAreaId == userSubAreaId.Value);
        }
        else if (userAreaId.HasValue && userAreaId.Value > 0)
        {
            query = query.Where(s => s.SubArea != null && s.SubArea.AreaId == userAreaId.Value);
        }

        var list = await query.ToListAsync();

        int total = list.Count;
        int present = list.Count(s => s.Status.StartsWith("Present") || s.Status == "Double Shift");
        int late = list.Count(s => s.LateByMinutes > 0);
        int early = list.Count(s => s.EarlyExitMinutes > 0);
        int doubleShift = list.Count(s => s.Status == "Double Shift");
        int singlePunch = list.Count(s => s.Status.StartsWith("Single Punch"));

        return new MusterRollKpiDto(total, present, late, early, doubleShift, singlePunch);
    }
}

