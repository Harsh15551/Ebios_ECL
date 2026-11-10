using System.Text.Json;
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
    private readonly IAuditService _auditService;
    private readonly ILogger<AttendanceProcessingService> _logger;

    public AttendanceProcessingService(
        AppDbContext context,
        IAuditService auditService,
        ILogger<AttendanceProcessingService> logger)
    {
        _context = context;
        _auditService = auditService;
        _logger = logger;
    }

    public Task ProcessPunchIntoMusterRollAsync(AttendanceLog punch)
    {
        return ProcessPunchIntoMusterRollAsync(punch, null);
    }

    public async Task ProcessPunchIntoMusterRollAsync(AttendanceLog punch, List<ShiftMaster>? cachedShifts)
    {
        if (punch == null || string.IsNullOrWhiteSpace(punch.EmpCode)) return;

        string cleanCode = punch.EmpCode.Trim().ToUpper();
        DateTime punchTime = punch.LogDate;
        TimeSpan punchTimeOfDay = punchTime.TimeOfDay;
        DateTime punchDate = DateTime.SpecifyKind(punchTime.Date, DateTimeKind.Utc);

        var shifts = cachedShifts ?? await _context.ShiftMasters.AsNoTracking().Where(s => s.IsActive).ToListAsync();

        // 1. Check for Open Night Shift from Yesterday (22:00 to 06:00 / 21:00 to 05:00)
        DateTime yesterdayDate = punchDate.AddDays(-1);
        var yesterdayNightSummary = await _context.AttendanceDailySummaries
            .FirstOrDefaultAsync(s => s.EmpCode.ToUpper() == cleanCode && 
                                      s.AttendanceDate == yesterdayDate && 
                                      s.ShiftId != null && 
                                      s.LastOutTime == null);

        if (yesterdayNightSummary != null)
        {
            var nightShift = shifts.FirstOrDefault(s => s.Id == yesterdayNightSummary.ShiftId) 
                          ?? shifts.FirstOrDefault(s => s.ShiftCode == yesterdayNightSummary.ShiftCode);

            if (nightShift != null && (nightShift.IsNightShift || nightShift.EndTime < nightShift.StartTime))
            {
                // Any subsequent morning punch within 16 hours of entry is the exit of yesterday's night shift
                if (punchTime > yesterdayNightSummary.FirstInTime && punchTime <= yesterdayNightSummary.FirstInTime.Value.AddHours(16))
                {
                    // This is the EXIT of yesterday's night shift!
                    yesterdayNightSummary.LastOutTime = punchTime;
                    yesterdayNightSummary.OutSerialNumber = punch.SerialNumber;
                    yesterdayNightSummary.OutDeviceName = punch.DeviceName;
                    yesterdayNightSummary.Shift = null;
                    CalculateSummaryMetrics(yesterdayNightSummary, nightShift);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Matched exit punch for night shift on {Date} for {Code} at {Machine}.", yesterdayDate, cleanCode, punch.DeviceName);
                    return;
                }
            }
        }

        // 2. Check for Existing Summary on Today's Date
        var todaySummary = await _context.AttendanceDailySummaries
            .FirstOrDefaultAsync(s => s.EmpCode.ToUpper() == cleanCode && s.AttendanceDate == punchDate);

        var existingEmp = punch.Employee ?? await _context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode.ToUpper() == cleanCode);
        bool isEmployeeKnown = existingEmp != null;
        int? resolvedEmpId = existingEmp?.Id ?? punch.EmployeeId;
        string empDisplayName = existingEmp?.FullName 
                             ?? (!string.IsNullOrWhiteSpace(punch.RawEmployeeName) ? punch.RawEmployeeName : $"Unregistered ({cleanCode})");

        if (todaySummary != null)
        {
            // Requirement 16: If this summary has been manually regularized by an authorized manager,
            // preserve the manual timing and audit trail. Do not overwrite with automated turnstile metrics.
            if (todaySummary.IsManuallyCorrected)
            {
                if (isEmployeeKnown && (!todaySummary.IsVerified || todaySummary.EmployeeId == null))
                {
                    todaySummary.EmployeeId = resolvedEmpId;
                    todaySummary.EmployeeName = empDisplayName;
                    todaySummary.Designation = existingEmp?.Designation;
                    todaySummary.IsVerified = true;
                    await _context.SaveChangesAsync();
                }
                return;
            }

            var shift = shifts.FirstOrDefault(s => s.Id == todaySummary.ShiftId) 
                     ?? shifts.FirstOrDefault(s => s.ShiftCode == todaySummary.ShiftCode);

            // Update employee metadata if newly identified
            if (isEmployeeKnown)
            {
                if (existingEmp?.IsHigherAuthority == true)
                {
                    todaySummary.IsHigherAuthority = true;
                }

                if (!todaySummary.IsVerified || todaySummary.EmployeeId == null)
                {
                    todaySummary.EmployeeId = resolvedEmpId;
                    todaySummary.EmployeeName = empDisplayName;
                    todaySummary.Designation = existingEmp?.Designation;
                    todaySummary.IsVerified = true;
                    todaySummary.IsFlagged = false;
                    if (todaySummary.Remarks != null && todaySummary.Remarks.Contains("Flagged"))
                    {
                        todaySummary.Remarks = todaySummary.RosterStatus == "Auto-Matched" ? $"Auto-matched to {todaySummary.ShiftCode}" : null;
                    }
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

                // If shift was auto-matched, re-resolve shift now that both entry and exit punches are known!
                if (todaySummary.RosterStatus == "Auto-Matched" && shifts.Any())
                {
                    var bestShift = ResolveShiftForPunch(todaySummary.FirstInTime.Value.TimeOfDay, shifts, todaySummary.LastOutTime.Value.TimeOfDay);
                    if (bestShift != null)
                    {
                        todaySummary.ShiftId = bestShift.Id;
                        todaySummary.ShiftCode = bestShift.ShiftCode;
                        todaySummary.Shift = null;
                        shift = bestShift;
                        if (todaySummary.Remarks != null && todaySummary.Remarks.StartsWith("Auto-matched"))
                        {
                            todaySummary.Remarks = $"Auto-matched to {bestShift.ShiftCode}";
                        }
                    }
                }

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

                if (todaySummary.RosterStatus == "Auto-Matched" && shifts.Any())
                {
                    var bestShift = ResolveShiftForPunch(todaySummary.FirstInTime.Value.TimeOfDay, shifts, todaySummary.LastOutTime?.TimeOfDay);
                    if (bestShift != null)
                    {
                        todaySummary.ShiftId = bestShift.Id;
                        todaySummary.ShiftCode = bestShift.ShiftCode;
                        todaySummary.Shift = null;
                        shift = bestShift;
                        if (todaySummary.Remarks != null && todaySummary.Remarks.StartsWith("Auto-matched"))
                        {
                            todaySummary.Remarks = $"Auto-matched to {bestShift.ShiftCode}";
                        }
                    }
                }

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
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmpCode.ToUpper() == cleanCode && r.RosterDate == punchDate);

        ShiftMaster? resolvedShift = roster != null
            ? shifts.FirstOrDefault(s => s.Id == roster.ShiftId) ?? shifts.FirstOrDefault(s => s.ShiftCode == roster.ShiftCode)
            : null;
        string rosterStatus = "Assigned";

        // Step B: If no roster assigned, auto-detect using employee-favorable window [StartTime - 35m, StartTime + 90m]
        if (resolvedShift == null && shifts.Any())
        {
            rosterStatus = "Auto-Matched";
            resolvedShift = ResolveShiftForPunch(punchTimeOfDay, shifts);
        }

        resolvedShift ??= shifts.FirstOrDefault(s => s.ShiftCode == "General") ?? shifts.FirstOrDefault();

        // 4. Assemble New Summary Record
        bool isHigherAuth = existingEmp?.IsHigherAuthority == true;

        var newSummary = new AttendanceDailySummary
        {
            AttendanceDate = punchDate,
            EmpCode = cleanCode,
            EmployeeName = empDisplayName,
            Designation = existingEmp?.Designation,
            EmployeeId = resolvedEmpId,
            SubAreaId = punch.SubAreaId ?? existingEmp?.SubAreaId,
            ShiftId = resolvedShift?.Id,
            ShiftCode = resolvedShift?.ShiftCode ?? "General",
            FirstInTime = punchTime,
            InSerialNumber = punch.SerialNumber,
            InDeviceName = punch.DeviceName,
            IsHigherAuthority = isHigherAuth,
            Status = isHigherAuth ? "Present" : "Single Punch",
            RosterStatus = rosterStatus,
            IsVerified = isEmployeeKnown,
            IsFlagged = !isEmployeeKnown,
            Remarks = !isEmployeeKnown ? "Flagged: Unregistered Worker" : (rosterStatus == "Auto-Matched" ? $"Auto-matched to {resolvedShift?.ShiftCode}" : null),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (isHigherAuth)
        {
            newSummary.Status = "Present";
            newSummary.TotalWorkHours = resolvedShift?.DurationHours ?? 8.0;
            newSummary.TotalWorkMinutes = (int)(newSummary.TotalWorkHours * 60);
            newSummary.LateByMinutes = 0;
            newSummary.EarlyInMinutes = 0;
            newSummary.EarlyExitMinutes = 0;
            newSummary.OvertimeMinutes = 0;
        }
        else if (resolvedShift != null)
        {
            double offset = GetCircularOffsetMinutes(punchTimeOfDay, resolvedShift.StartTime);
            int grace = resolvedShift.GraceMinutes > 0 ? resolvedShift.GraceMinutes : 30;

            if (offset <= grace)
            {
                newSummary.LateByMinutes = 0;
            }
            else
            {
                newSummary.LateByMinutes = (int)Math.Round(offset);
            }

            double earlyBoundary = (resolvedShift.IsNightShift || resolvedShift.EndTime < resolvedShift.StartTime) ? -90 : -60;
            if (offset < earlyBoundary)
            {
                newSummary.EarlyInMinutes = (int)Math.Round(-earlyBoundary - offset);
            }
            else
            {
                newSummary.EarlyInMinutes = 0;
            }

            if (newSummary.LateByMinutes > 0)
            {
                newSummary.Status = "Single Punch (Late)";
            }
            else if (newSummary.EarlyInMinutes > 0)
            {
                newSummary.Status = "Single Punch (Early)";
            }
            else
            {
                newSummary.Status = "Single Punch";
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

        // For Higher Authorities, record actual in/out punches, but always mark clean 'Present' with 0 timing penalties
        if (summary.IsHigherAuthority)
        {
            summary.Status = "Present";
            summary.LateByMinutes = 0;
            summary.EarlyInMinutes = 0;
            summary.EarlyExitMinutes = 0;
            summary.OvertimeMinutes = 0;
            return;
        }

        // Evaluate Early Entry / Late Entry:
        // Reporting window: StartTime - 30 minutes to StartTime + GraceMinutes (30 mins)
        TimeSpan inTimeOfDay = summary.FirstInTime.Value.TimeOfDay;
        double inOffset = GetCircularOffsetMinutes(inTimeOfDay, shift.StartTime);
        int grace = shift.GraceMinutes > 0 ? shift.GraceMinutes : 30;

        if (inOffset <= grace)
        {
            summary.LateByMinutes = 0;
        }
        else
        {
            summary.LateByMinutes = (int)Math.Round(inOffset);
        }

        double earlyBoundary = (shift.IsNightShift || shift.EndTime < shift.StartTime) ? -90 : -60;
        if (inOffset < earlyBoundary)
        {
            summary.EarlyInMinutes = (int)Math.Round(-earlyBoundary - inOffset);
        }
        else
        {
            summary.EarlyInMinutes = 0;
        }

        // Evaluate Early Exit (No artificial OutWindow restrictions - measured strictly against scheduled Shift EndTime)
        TimeSpan outTimeOfDay = summary.LastOutTime.Value.TimeOfDay;
        double exitOffset = GetCircularOffsetMinutes(outTimeOfDay, shift.EndTime);
        if (exitOffset < 0)
        {
            summary.EarlyExitMinutes = (int)Math.Round(-exitOffset);
        }
        else
        {
            summary.EarlyExitMinutes = 0;
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
            if (summary.LateByMinutes > 0)
                summary.Status = "Present (Late)";
            else if (summary.EarlyInMinutes > 0)
                summary.Status = "Present (Early)";
            else
                summary.Status = "Present";
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
        // REQUIREMENT 16: Exclude IsManuallyCorrected records so approved manual regularizations are NEVER wiped!
        var existingSummariesQuery = _context.AttendanceDailySummaries
            .Where(s => s.AttendanceDate >= fromUtc && s.AttendanceDate <= toUtc && !s.IsManuallyCorrected);

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
        var allShifts = await _context.ShiftMasters.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        int count = 0;

        foreach (var p in punches)
        {
            await ProcessPunchIntoMusterRollAsync(p, allShifts);
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
                s.Designation ?? (s.Employee != null ? s.Employee.Designation : "Colliery Worker"),
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
                s.EarlyInMinutes,
                s.EarlyExitMinutes,
                s.Status,
                s.RosterStatus,
                s.Remarks,
                s.IsVerified,
                s.IsFlagged,
                s.IsHigherAuthority || (s.Employee != null && s.Employee.IsHigherAuthority),
                s.IsManuallyCorrected,
                s.CorrectionReason,
                s.CorrectedBy,
                s.CorrectedAt,
                s.IsSyncedToSap,
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

    public async Task<(bool Success, string Message)> RegularizeAttendanceAsync(RegularizeAttendanceDto dto, int? callerAreaId = null, int? callerSubAreaId = null)
    {
        var summary = await _context.AttendanceDailySummaries
            .Include(s => s.Employee)
                .ThenInclude(e => e!.SubArea)
            .Include(s => s.SubArea)
            .FirstOrDefaultAsync(s => s.Id == dto.SummaryId);

        if (summary == null)
        {
            return (false, "Attendance record not found.");
        }

        // Jurisdiction enforcement: Verify caller has authority over this worker
        if (callerSubAreaId.HasValue && callerSubAreaId.Value > 0)
        {
            int? workerSubAreaId = summary.SubAreaId ?? summary.Employee?.SubAreaId;
            if (workerSubAreaId != callerSubAreaId.Value)
            {
                return (false, $"Permission Denied: Worker '{summary.EmpCode}' belongs to a different colliery unit.");
            }
        }
        else if (callerAreaId.HasValue && callerAreaId.Value > 0)
        {
            int? workerAreaId = summary.SubArea?.AreaId ?? summary.Employee?.SubArea?.AreaId;
            if (workerAreaId != callerAreaId.Value)
            {
                return (false, $"Permission Denied: Worker '{summary.EmpCode}' is outside your assigned mining area jurisdiction.");
            }
        }

        bool isOverwrite = summary.IsManuallyCorrected;
        string? previousOfficer = summary.CorrectedBy;
        string? oldStateJson = isOverwrite ? JsonSerializer.Serialize(new
        {
            FirstInTime = summary.FirstInTime?.ToString("HH:mm:ss"),
            LastOutTime = summary.LastOutTime?.ToString("HH:mm:ss"),
            ShiftCode = summary.ShiftCode,
            CorrectionReason = summary.CorrectionReason,
            CorrectedBy = summary.CorrectedBy,
            CorrectedAt = summary.CorrectedAt
        }) : null;

        if (dto.InTime.HasValue)
        {
            summary.FirstInTime = DateTime.SpecifyKind(dto.InTime.Value, DateTimeKind.Utc);
            if (string.IsNullOrEmpty(summary.InSerialNumber) || summary.InSerialNumber == "MANUAL")
            {
                summary.InSerialNumber = "MANUAL";
                summary.InDeviceName = "Manual Regularization";
            }
        }

        if (dto.OutTime.HasValue)
        {
            summary.LastOutTime = DateTime.SpecifyKind(dto.OutTime.Value, DateTimeKind.Utc);
            if (string.IsNullOrEmpty(summary.OutSerialNumber) || summary.OutSerialNumber == "MANUAL")
            {
                summary.OutSerialNumber = "MANUAL";
                summary.OutDeviceName = "Manual Regularization";
            }
        }

        summary.IsManuallyCorrected = true;
        summary.CorrectionReason = !string.IsNullOrWhiteSpace(dto.Reason) ? dto.Reason.Trim() : "Manual Administrative Correction";
        summary.CorrectedBy = dto.CorrectedBy;
        summary.CorrectedAt = DateTime.UtcNow;
        summary.IsSyncedToSap = false;
        summary.UpdatedAt = DateTime.UtcNow;

        // If manager selected a different shift, update the shift mapping
        ShiftMaster? shift = null;
        if (!string.IsNullOrWhiteSpace(dto.ShiftCode) && !dto.ShiftCode.Equals(summary.ShiftCode, StringComparison.OrdinalIgnoreCase))
        {
            var newShift = await _context.ShiftMasters.AsNoTracking().FirstOrDefaultAsync(s => s.ShiftCode == dto.ShiftCode);
            if (newShift != null)
            {
                summary.ShiftId = newShift.Id;
                summary.ShiftCode = newShift.ShiftCode;
                summary.Shift = null;
                summary.RosterStatus = "Manual-Overridden";
                shift = newShift;
            }
        }

        shift ??= await _context.ShiftMasters.AsNoTracking().FirstOrDefaultAsync(s => s.ShiftCode == summary.ShiftCode);

        if (summary.FirstInTime.HasValue && summary.LastOutTime.HasValue)
        {
            if (shift != null)
            {
                CalculateSummaryMetrics(summary, shift);
            }
            else
            {
                int totalMinutes = (int)(summary.LastOutTime.Value - summary.FirstInTime.Value).TotalMinutes;
                summary.TotalWorkMinutes = Math.Max(0, totalMinutes);
                summary.TotalWorkHours = Math.Round(summary.TotalWorkMinutes / 60.0, 2);
                summary.Status = summary.TotalWorkHours >= 7.5 ? "Present" : "Half Day";
            }

            if (summary.Status.StartsWith("Single Punch"))
            {
                summary.Status = summary.TotalWorkHours >= 4.0 ? "Present" : "Half Day";
            }
        }
        else if (summary.IsHigherAuthority && (summary.FirstInTime.HasValue || summary.LastOutTime.HasValue))
        {
            summary.Status = "Present";
            summary.TotalWorkHours = shift?.DurationHours ?? 8.0;
            summary.TotalWorkMinutes = (int)(summary.TotalWorkHours * 60);
            summary.LateByMinutes = 0;
            summary.EarlyInMinutes = 0;
            summary.EarlyExitMinutes = 0;
            summary.OvertimeMinutes = 0;
        }

        await _context.SaveChangesAsync();

        string newStateJson = JsonSerializer.Serialize(new
        {
            FirstInTime = summary.FirstInTime?.ToString("HH:mm:ss"),
            LastOutTime = summary.LastOutTime?.ToString("HH:mm:ss"),
            ShiftCode = summary.ShiftCode,
            CorrectionReason = summary.CorrectionReason,
            CorrectedBy = summary.CorrectedBy,
            CorrectedAt = summary.CorrectedAt
        });

        string auditDetails = isOverwrite
            ? $"Previous regularization by '{previousOfficer}' overwritten by '{dto.CorrectedBy}'. Reason: {dto.Reason}"
            : $"Attendance regularized by '{dto.CorrectedBy}'. Reason: {dto.Reason}";

        await _auditService.LogRegularizationAsync(
            summary.EmpCode,
            summary.AttendanceDate,
            oldStateJson,
            newStateJson,
            isOverwrite,
            auditDetails,
            null,
            dto.CorrectedBy,
            dto.CorrectedBy,
            "Manager");

        _logger.LogInformation("Regularized attendance record {Id} for worker {EmpCode} by {User}. Reason: {Reason}", summary.Id, summary.EmpCode, dto.CorrectedBy, dto.Reason);
        return (true, $"Attendance for {summary.EmpCode} on {summary.AttendanceDate:dd-MM-yyyy} regularized successfully.");
    }

    public async Task<(bool Success, string Message)> RevertToMachinePunchesAsync(int summaryId, int? callerAreaId = null, int? callerSubAreaId = null)
    {
        var summary = await _context.AttendanceDailySummaries
            .Include(s => s.Employee)
                .ThenInclude(e => e!.SubArea)
            .Include(s => s.SubArea)
            .FirstOrDefaultAsync(s => s.Id == summaryId);

        if (summary == null)
        {
            return (false, "Attendance record not found.");
        }

        // Jurisdiction enforcement: Verify caller has authority over this worker
        if (callerSubAreaId.HasValue && callerSubAreaId.Value > 0)
        {
            int? workerSubAreaId = summary.SubAreaId ?? summary.Employee?.SubAreaId;
            if (workerSubAreaId != callerSubAreaId.Value)
            {
                return (false, $"Permission Denied: Worker '{summary.EmpCode}' belongs to a different colliery unit.");
            }
        }
        else if (callerAreaId.HasValue && callerAreaId.Value > 0)
        {
            int? workerAreaId = summary.SubArea?.AreaId ?? summary.Employee?.SubArea?.AreaId;
            if (workerAreaId != callerAreaId.Value)
            {
                return (false, $"Permission Denied: Worker '{summary.EmpCode}' is outside your assigned mining area jurisdiction.");
            }
        }

        string? previousOfficer = summary.CorrectedBy;
        string? oldStateJson = JsonSerializer.Serialize(new
        {
            FirstInTime = summary.FirstInTime?.ToString("HH:mm:ss"),
            LastOutTime = summary.LastOutTime?.ToString("HH:mm:ss"),
            ShiftCode = summary.ShiftCode,
            CorrectionReason = summary.CorrectionReason,
            CorrectedBy = summary.CorrectedBy,
            CorrectedAt = summary.CorrectedAt
        });

        var startOfDay = DateTime.SpecifyKind(summary.AttendanceDate.Date, DateTimeKind.Utc);
        var endOfDay = DateTime.SpecifyKind(summary.AttendanceDate.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

        var punches = await _context.AttendanceLogs
            .Include(l => l.Device)
            .Where(l => l.EmpCode.ToUpper() == summary.EmpCode.ToUpper() && l.LogDate >= startOfDay && l.LogDate <= endOfDay)
            .OrderBy(l => l.LogDate)
            .ToListAsync();

        var allShifts = await _context.ShiftMasters.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var roster = await _context.EmployeeShiftRosters
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmpCode.ToUpper() == summary.EmpCode.ToUpper() && r.RosterDate == startOfDay);

        if (punches.Count == 0)
        {
            summary.FirstInTime = null;
            summary.InSerialNumber = null;
            summary.InDeviceName = null;
            summary.LastOutTime = null;
            summary.OutSerialNumber = null;
            summary.OutDeviceName = null;
            summary.TotalWorkMinutes = 0;
            summary.TotalWorkHours = 0;
            summary.LateByMinutes = 0;
            summary.EarlyInMinutes = 0;
            summary.EarlyExitMinutes = 0;
            summary.OvertimeMinutes = 0;
            summary.Status = "Absent";
        }
        else if (punches.Count == 1)
        {
            var p = punches[0];
            summary.FirstInTime = p.LogDate;
            summary.InSerialNumber = p.SerialNumber;
            summary.InDeviceName = p.DeviceName;
            summary.LastOutTime = null;
            summary.OutSerialNumber = null;
            summary.OutDeviceName = null;
            summary.TotalWorkMinutes = 0;
            summary.TotalWorkHours = 0;
            summary.EarlyExitMinutes = 0;
            summary.OvertimeMinutes = 0;

            ShiftMaster? shift = roster != null
                ? allShifts.FirstOrDefault(s => s.Id == roster.ShiftId) ?? allShifts.FirstOrDefault(s => s.ShiftCode == roster.ShiftCode)
                : null;
            if (shift != null)
            {
                summary.ShiftId = shift.Id;
                summary.ShiftCode = shift.ShiftCode;
                summary.Shift = null;
                summary.RosterStatus = "Assigned";
            }
            else
            {
                summary.RosterStatus = "Auto-Matched";
                shift = ResolveShiftForPunch(p.LogDate.TimeOfDay, allShifts);
                if (shift != null)
                {
                    summary.ShiftId = shift.Id;
                    summary.ShiftCode = shift.ShiftCode;
                    summary.Shift = null;
                }
            }

            if (summary.IsHigherAuthority)
            {
                summary.Status = "Present";
                summary.LateByMinutes = 0;
                summary.EarlyInMinutes = 0;
            }
            else if (shift != null)
            {
                double offset = GetCircularOffsetMinutes(p.LogDate.TimeOfDay, shift.StartTime);
                int grace = shift.GraceMinutes > 0 ? shift.GraceMinutes : 30;
                summary.LateByMinutes = offset > grace ? (int)Math.Round(offset) : 0;
                double earlyBoundary = (shift.IsNightShift || shift.EndTime < shift.StartTime) ? -90 : -60;
                summary.EarlyInMinutes = offset < earlyBoundary ? (int)Math.Round(-earlyBoundary - offset) : 0;
                summary.Status = summary.LateByMinutes > 0 ? "Single Punch (Late)" : (summary.EarlyInMinutes > 0 ? "Single Punch (Early)" : "Single Punch");
            }
            else
            {
                summary.Status = "Single Punch";
                summary.LateByMinutes = 0;
                summary.EarlyInMinutes = 0;
            }
        }
        else
        {
            var firstP = punches.First();
            var lastP = punches.Last();
            summary.FirstInTime = firstP.LogDate;
            summary.InSerialNumber = firstP.SerialNumber;
            summary.InDeviceName = firstP.DeviceName;
            summary.LastOutTime = lastP.LogDate;
            summary.OutSerialNumber = lastP.SerialNumber;
            summary.OutDeviceName = lastP.DeviceName;

            ShiftMaster? shift = roster != null
                ? allShifts.FirstOrDefault(s => s.Id == roster.ShiftId) ?? allShifts.FirstOrDefault(s => s.ShiftCode == roster.ShiftCode)
                : null;
            if (shift != null)
            {
                summary.ShiftId = shift.Id;
                summary.ShiftCode = shift.ShiftCode;
                summary.Shift = null;
                summary.RosterStatus = "Assigned";
            }
            else
            {
                summary.RosterStatus = "Auto-Matched";
                shift = ResolveShiftForPunch(firstP.LogDate.TimeOfDay, allShifts, lastP.LogDate.TimeOfDay);
                if (shift != null)
                {
                    summary.ShiftId = shift.Id;
                    summary.ShiftCode = shift.ShiftCode;
                    summary.Shift = null;
                }
            }

            if (shift != null)
            {
                CalculateSummaryMetrics(summary, shift);
            }
        }

        summary.IsManuallyCorrected = false;
        summary.CorrectionReason = null;
        summary.CorrectedBy = null;
        summary.CorrectedAt = null;
        summary.IsSyncedToSap = false;
        summary.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        string newStateJson = JsonSerializer.Serialize(new
        {
            FirstInTime = summary.FirstInTime?.ToString("HH:mm:ss"),
            LastOutTime = summary.LastOutTime?.ToString("HH:mm:ss"),
            ShiftCode = summary.ShiftCode,
            Status = summary.Status,
            IsManuallyCorrected = false
        });

        await _auditService.LogRegularizationAsync(
            summary.EmpCode,
            summary.AttendanceDate,
            oldStateJson,
            newStateJson,
            isOverwrite: true,
            details: $"Manual regularization previously set by '{previousOfficer}' was reverted back to raw machine punches.",
            userId: null,
            username: "Officer",
            officerName: "Officer",
            role: "Manager");

        _logger.LogInformation("Reverted attendance record {Id} for worker {EmpCode} to machine punches.", summary.Id, summary.EmpCode);
        return (true, $"Attendance for {summary.EmpCode} reverted to turnstile machine punches.");
    }

    public async Task<BulkRegularizeResultDto> BulkRegularizeAsync(
        List<BulkRegularizeEntryDto> entries, 
        string adminUsername,
        int? callerAreaId = null,
        int? callerSubAreaId = null)
    {
        int successCount = 0;
        var errors = new List<string>();
        var pendingAuditLogs = new List<(string EmpCode, DateTime Date, string? OldJson, string NewJson, bool IsOverwrite, string Details)>();

        var allShifts = await _context.ShiftMasters.AsNoTracking().Where(s => s.IsActive).ToListAsync();

        foreach (var entry in entries)
        {
            try
            {
                string cleanCode = entry.EmpCode.Trim().ToUpper();
                DateTime targetDate = DateTime.SpecifyKind(entry.Date.Date, DateTimeKind.Utc);

                var emp = await _context.Employees
                    .Include(e => e.SubArea)
                    .FirstOrDefaultAsync(e => e.EmpCode.ToUpper() == cleanCode);

                var summary = await _context.AttendanceDailySummaries
                    .Include(s => s.SubArea)
                    .FirstOrDefaultAsync(s => s.EmpCode.ToUpper() == cleanCode && s.AttendanceDate == targetDate);

                // Jurisdiction enforcement: Ensure caller has authority over this worker or their attendance location
                if (callerSubAreaId.HasValue && callerSubAreaId.Value > 0)
                {
                    int? targetSubAreaId = emp?.SubAreaId ?? summary?.SubAreaId;
                    if (targetSubAreaId.HasValue && targetSubAreaId.Value != callerSubAreaId.Value)
                    {
                        errors.Add($"Worker '{cleanCode}': Skipped - Outside your assigned colliery unit jurisdiction.");
                        continue;
                    }
                }
                else if (callerAreaId.HasValue && callerAreaId.Value > 0)
                {
                    int? targetAreaId = emp?.SubArea?.AreaId ?? summary?.SubArea?.AreaId;
                    if (targetAreaId.HasValue && targetAreaId.Value != callerAreaId.Value)
                    {
                        errors.Add($"Worker '{cleanCode}': Skipped - Outside your assigned mining area jurisdiction.");
                        continue;
                    }
                }

                bool isOverwrite = summary?.IsManuallyCorrected == true;
                string? previousOfficer = summary?.CorrectedBy;
                string? oldStateJson = isOverwrite && summary != null ? JsonSerializer.Serialize(new
                {
                    FirstInTime = summary.FirstInTime?.ToString("HH:mm:ss"),
                    LastOutTime = summary.LastOutTime?.ToString("HH:mm:ss"),
                    ShiftCode = summary.ShiftCode,
                    CorrectionReason = summary.CorrectionReason,
                    CorrectedBy = summary.CorrectedBy,
                    CorrectedAt = summary.CorrectedAt
                }) : null;

                if (summary == null)
                {
                    ShiftMaster? shift = null;
                    if (!string.IsNullOrWhiteSpace(entry.ShiftCode))
                    {
                        shift = allShifts.FirstOrDefault(s => 
                            s.ShiftCode.Equals(entry.ShiftCode.Trim(), StringComparison.OrdinalIgnoreCase) ||
                            s.ShiftName.Equals(entry.ShiftCode.Trim(), StringComparison.OrdinalIgnoreCase));
                    }

                    if (shift == null)
                    {
                        var roster = await _context.EmployeeShiftRosters
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.EmpCode.ToUpper() == cleanCode && r.RosterDate == targetDate);

                        shift = roster != null
                            ? allShifts.FirstOrDefault(s => s.Id == roster.ShiftId) ?? allShifts.FirstOrDefault(s => s.ShiftCode == roster.ShiftCode)
                            : null;
                    }

                    if (shift == null && entry.InTime.HasValue)
                    {
                        shift = ResolveShiftForPunch(entry.InTime.Value.TimeOfDay, allShifts, entry.OutTime?.TimeOfDay);
                    }

                    shift ??= allShifts.FirstOrDefault(s => s.ShiftCode == "General") ?? allShifts.FirstOrDefault();

                    string initialRosterStatus = !string.IsNullOrWhiteSpace(entry.ShiftCode) && shift?.ShiftCode.Equals(entry.ShiftCode.Trim(), StringComparison.OrdinalIgnoreCase) == true
                        ? "Manual-Overridden"
                        : "Auto-Matched";

                    summary = new AttendanceDailySummary
                    {
                        AttendanceDate = targetDate,
                        EmpCode = cleanCode,
                        EmployeeName = emp?.FullName ?? cleanCode,
                        Designation = emp?.Designation ?? "Colliery Worker",
                        EmployeeId = emp?.Id,
                        SubAreaId = emp?.SubAreaId,
                        ShiftId = shift?.Id,
                        ShiftCode = shift?.ShiftCode ?? "General",
                        IsHigherAuthority = emp?.IsHigherAuthority == true,
                        IsVerified = emp != null,
                        IsFlagged = emp == null,
                        Status = emp?.IsHigherAuthority == true ? "Present" : "Single Punch",
                        RosterStatus = initialRosterStatus,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.AttendanceDailySummaries.Add(summary);
                }

                if (entry.InTime.HasValue)
                {
                    summary.FirstInTime = DateTime.SpecifyKind(entry.InTime.Value, DateTimeKind.Utc);
                    if (string.IsNullOrEmpty(summary.InSerialNumber) || summary.InSerialNumber == "MANUAL")
                    {
                        summary.InSerialNumber = "MANUAL";
                        summary.InDeviceName = "Manual Regularization";
                    }
                }

                if (entry.OutTime.HasValue)
                {
                    summary.LastOutTime = DateTime.SpecifyKind(entry.OutTime.Value, DateTimeKind.Utc);
                    if (string.IsNullOrEmpty(summary.OutSerialNumber) || summary.OutSerialNumber == "MANUAL")
                    {
                        summary.OutSerialNumber = "MANUAL";
                        summary.OutDeviceName = "Manual Regularization";
                    }
                }

                summary.IsManuallyCorrected = true;
                summary.CorrectionReason = !string.IsNullOrWhiteSpace(entry.Reason) ? entry.Reason.Trim() : "Manual Administrative Regularization";
                summary.CorrectedBy = adminUsername;
                summary.CorrectedAt = DateTime.UtcNow;
                summary.IsSyncedToSap = false;
                summary.UpdatedAt = DateTime.UtcNow;

                // Explicit ShiftCode matching: If given and matches, use it; otherwise auto-fetch algorithm resolves
                ShiftMaster? explicitShift = null;
                if (!string.IsNullOrWhiteSpace(entry.ShiftCode))
                {
                    explicitShift = allShifts.FirstOrDefault(s => 
                        s.ShiftCode.Equals(entry.ShiftCode.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        s.ShiftName.Equals(entry.ShiftCode.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (explicitShift != null)
                {
                    summary.ShiftId = explicitShift.Id;
                    summary.ShiftCode = explicitShift.ShiftCode;
                    summary.Shift = null;
                    summary.RosterStatus = "Manual-Overridden";
                }
                else if (summary.FirstInTime.HasValue)
                {
                    // Auto-resolve best matching shift using punch timings (e.g. Shift III for 22:00 -> 06:00)
                    var autoShift = ResolveShiftForPunch(
                        summary.FirstInTime.Value.TimeOfDay,
                        allShifts,
                        summary.LastOutTime?.TimeOfDay);

                    if (autoShift != null)
                    {
                        summary.ShiftId = autoShift.Id;
                        summary.ShiftCode = autoShift.ShiftCode;
                        summary.Shift = null;
                        summary.RosterStatus = "Auto-Matched";
                    }
                }

                var currentShift = allShifts.FirstOrDefault(s => s.Id == summary.ShiftId) 
                                ?? allShifts.FirstOrDefault(s => s.ShiftCode == summary.ShiftCode);
                if (summary.FirstInTime.HasValue && summary.LastOutTime.HasValue && currentShift != null)
                {
                    CalculateSummaryMetrics(summary, currentShift);
                    if (summary.Status.StartsWith("Single Punch"))
                    {
                        summary.Status = summary.TotalWorkHours >= 4.0 ? "Present" : "Half Day";
                    }
                }
                else if (summary.IsHigherAuthority && (summary.FirstInTime.HasValue || summary.LastOutTime.HasValue))
                {
                    summary.Status = "Present";
                }

                string newStateJson = JsonSerializer.Serialize(new
                {
                    FirstInTime = summary.FirstInTime?.ToString("HH:mm:ss"),
                    LastOutTime = summary.LastOutTime?.ToString("HH:mm:ss"),
                    ShiftCode = summary.ShiftCode,
                    CorrectionReason = summary.CorrectionReason,
                    CorrectedBy = adminUsername,
                    CorrectedAt = summary.CorrectedAt
                });

                string details = isOverwrite
                    ? $"Bulk CSV: Regularization by '{previousOfficer}' was overwritten by '{adminUsername}'. Reason: {entry.Reason}"
                    : $"Bulk CSV: Regularized by '{adminUsername}'. Reason: {entry.Reason}";

                pendingAuditLogs.Add((cleanCode, targetDate, oldStateJson, newStateJson, isOverwrite, details));
                successCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"Row {entry.EmpCode} ({entry.Date:dd-MM-yyyy}): {ex.Message}");
            }
        }

        if (successCount > 0)
        {
            await _context.SaveChangesAsync();

            // Persist audit trail for all modified records
            foreach (var log in pendingAuditLogs)
            {
                await _auditService.LogRegularizationAsync(
                    log.EmpCode,
                    log.Date,
                    log.OldJson,
                    log.NewJson,
                    log.IsOverwrite,
                    log.Details,
                    null,
                    adminUsername,
                    adminUsername,
                    "Manager");
            }
        }

        return new BulkRegularizeResultDto(successCount, entries.Count, errors);
    }

    public static double GetCircularOffsetMinutes(TimeSpan time, TimeSpan referenceTime)
    {
        double diff = (time - referenceTime).TotalMinutes;
        while (diff > 720) diff -= 1440;
        while (diff < -720) diff += 1440;
        return diff;
    }

    public static ShiftMaster? ResolveShiftForPunch(
        TimeSpan punchTimeOfDay,
        IEnumerable<ShiftMaster> shifts,
        TimeSpan? outTimeOfDay = null)
    {
        var activeShifts = shifts.Where(s => s.IsActive).ToList();
        if (!activeShifts.Any()) return null;

        var evaluated = activeShifts.Select(s =>
        {
            bool isCrossMidnightOrNight = s.IsNightShift || s.EndTime < s.StartTime;
            double offset = GetCircularOffsetMinutes(punchTimeOfDay, s.StartTime);
            
            // Colliery shift reporting window:
            // For night/cross-midnight shifts: 90 mins early (lamp room preparation) to 120 mins late
            // For standard day shifts: 60 mins early to 90 mins late
            double earlyWindow = isCrossMidnightOrNight ? -90 : -60;
            double lateWindow = isCrossMidnightOrNight ? 120 : 90;
            bool inWindow = offset >= earlyWindow && offset <= lateWindow;

            int grace = s.GraceMinutes > 0 ? s.GraceMinutes : 30;
            int lateMinutes = offset > grace ? (int)Math.Round(offset) : 0;
            
            double earlyBoundary = isCrossMidnightOrNight ? -90 : -60;
            int earlyMinutes = offset < earlyBoundary ? (int)Math.Round(-earlyBoundary - offset) : 0;

            double outDiff = 0;
            if (outTimeOfDay.HasValue)
            {
                outDiff = Math.Abs(GetCircularOffsetMinutes(outTimeOfDay.Value, s.EndTime));
            }

            return new
            {
                Shift = s,
                InWindow = inWindow,
                LateMinutes = lateMinutes,
                EarlyMinutes = earlyMinutes,
                IsOnTime = lateMinutes == 0 && earlyMinutes == 0,
                IsReportingBeforeStart = offset <= 0,
                AbsOffset = Math.Abs(offset),
                OutDiff = outDiff,
                IsGeneralPreference = s.ShiftCode.StartsWith("G") || s.ShiftCode == "General"
            };
        }).ToList();

        var candidates = evaluated.Where(e => e.InWindow).ToList();
        if (!candidates.Any())
        {
            candidates = evaluated;
        }

        var best = candidates
            .OrderBy(c => c.InWindow ? 0 : 1)
            .ThenBy(c => outTimeOfDay.HasValue ? (c.AbsOffset + c.OutDiff) : 0)
            .ThenBy(c => c.AbsOffset)
            .ThenBy(c => c.LateMinutes)
            .ThenBy(c => c.EarlyMinutes)
            .ThenBy(c => c.IsGeneralPreference ? 0 : 1)
            .ThenBy(c => c.Shift.ShiftCode)
            .FirstOrDefault();

        return best?.Shift;
    }
}

