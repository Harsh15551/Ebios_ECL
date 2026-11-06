using System.Text.Json;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class RosterService : IRosterService
{
    private readonly AppDbContext _context;
    private readonly IAuditService _auditService;
    private readonly ILogger<RosterService> _logger;

    public RosterService(
        AppDbContext context, 
        IAuditService auditService,
        ILogger<RosterService> logger)
    {
        _context = context;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<List<EmployeeShiftRosterDto>> GetRosterAsync(DateTime date, int? areaId = null, int? subAreaId = null)
    {
        var targetDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var query = _context.EmployeeShiftRosters
            .AsNoTracking()
            .Include(r => r.Employee)
            .ThenInclude(e => e!.SubArea)
            .ThenInclude(s => s!.Area)
            .Include(r => r.Shift)
            .Where(r => r.RosterDate == targetDate);

        if (subAreaId.HasValue && subAreaId.Value > 0)
        {
            query = query.Where(r => r.Employee != null && r.Employee.SubAreaId == subAreaId.Value);
        }
        else if (areaId.HasValue && areaId.Value > 0)
        {
            query = query.Where(r => r.Employee != null && r.Employee.SubArea != null && r.Employee.SubArea.AreaId == areaId.Value);
        }

        return await query
            .OrderBy(r => r.EmpCode)
            .Select(r => new EmployeeShiftRosterDto(
                r.Id,
                r.EmployeeId,
                r.EmpCode,
                r.Employee != null ? r.Employee.FullName : r.EmpCode,
                r.Employee != null && r.Employee.SubArea != null ? r.Employee.SubArea.SubAreaName : "—",
                r.RosterDate,
                r.ShiftId,
                r.ShiftCode,
                r.Shift != null ? r.Shift.ShiftName : r.ShiftCode,
                r.AssignedBy,
                r.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<bool> AssignShiftAsync(
        AssignRosterDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null,
        bool skipAudit = false)
    {
        if (string.IsNullOrWhiteSpace(dto.EmpCode) || string.IsNullOrWhiteSpace(dto.ShiftCode))
            return false;

        string cleanCode = dto.EmpCode.Trim().ToUpper();
        string cleanShift = dto.ShiftCode.Trim();
        var targetDate = DateTime.SpecifyKind(dto.RosterDate.Date, DateTimeKind.Utc);

        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmpCode.ToUpper() == cleanCode);
        if (employee == null) return false;

        var shift = await _context.ShiftMasters.FirstOrDefaultAsync(s => s.ShiftCode.ToUpper() == cleanShift.ToUpper());
        if (shift == null) return false;

        var existing = await _context.EmployeeShiftRosters
            .FirstOrDefaultAsync(r => r.EmpCode.ToUpper() == cleanCode && r.RosterDate == targetDate);

        string? oldStateJson = null;
        if (existing != null)
        {
            oldStateJson = JsonSerializer.Serialize(new
            {
                EmpCode = cleanCode,
                ShiftCode = existing.ShiftCode,
                RosterDate = existing.RosterDate.ToString("yyyy-MM-dd"),
                AssignedBy = existing.AssignedBy
            });

            existing.ShiftId = shift.Id;
            existing.ShiftCode = shift.ShiftCode;
            existing.AssignedBy = dto.AssignedBy;
        }
        else
        {
            var roster = new EmployeeShiftRoster
            {
                EmployeeId = employee.Id,
                EmpCode = cleanCode,
                RosterDate = targetDate,
                ShiftId = shift.Id,
                ShiftCode = shift.ShiftCode,
                AssignedBy = dto.AssignedBy,
                CreatedAt = DateTime.UtcNow
            };
            _context.EmployeeShiftRosters.Add(roster);
        }

        // Retroactively update existing attendance summary if punches already occurred on this date
        var existingSummary = await _context.AttendanceDailySummaries
            .FirstOrDefaultAsync(s => s.EmpCode.ToUpper() == cleanCode && s.AttendanceDate == targetDate);

        if (existingSummary != null)
        {
            existingSummary.ShiftId = shift.Id;
            existingSummary.ShiftCode = shift.ShiftCode;
            existingSummary.RosterStatus = "Assigned";
            existingSummary.Remarks = $"Assigned to {shift.ShiftCode}";

            if (existingSummary.FirstInTime.HasValue)
            {
                TimeSpan inTime = existingSummary.FirstInTime.Value.TimeOfDay;
                TimeSpan permissibleLate = shift.StartTime.Add(TimeSpan.FromMinutes(shift.GraceMinutes));
                if (inTime > permissibleLate)
                {
                    existingSummary.LateByMinutes = (int)(inTime - shift.StartTime).TotalMinutes;
                }
                else
                {
                    existingSummary.LateByMinutes = 0;
                }

                if (existingSummary.LastOutTime.HasValue)
                {
                    int totalMinutes = (int)(existingSummary.LastOutTime.Value - existingSummary.FirstInTime.Value).TotalMinutes;
                    if (totalMinutes < 0) totalMinutes = 0;
                    existingSummary.TotalWorkMinutes = totalMinutes;
                    existingSummary.TotalWorkHours = Math.Round(totalMinutes / 60.0, 2);

                    TimeSpan outTime = existingSummary.LastOutTime.Value.TimeOfDay;
                    if (outTime < shift.EndTime)
                    {
                        existingSummary.EarlyExitMinutes = (int)(shift.EndTime - outTime).TotalMinutes;
                    }
                    else
                    {
                        existingSummary.EarlyExitMinutes = 0;
                    }

                    int stdMinutes = (int)(shift.DurationHours * 60);
                    existingSummary.OvertimeMinutes = totalMinutes > stdMinutes ? totalMinutes - stdMinutes : 0;

                    if (existingSummary.TotalWorkHours >= 14.0)
                    {
                        existingSummary.Status = "Double Shift";
                    }
                    else if (existingSummary.TotalWorkHours >= 7.5)
                    {
                        existingSummary.Status = existingSummary.LateByMinutes > 0 ? "Present (Late)" : "Present";
                    }
                    else if (existingSummary.TotalWorkHours >= 4.0)
                    {
                        existingSummary.Status = "Half Day";
                    }
                    else
                    {
                        existingSummary.Status = "Short Hours";
                    }
                }
                else
                {
                    existingSummary.Status = existingSummary.LateByMinutes > 0 ? "Single Punch (Late)" : "Single Punch";
                }
            }
        }

        await _context.SaveChangesAsync();

        if (!skipAudit)
        {
            var newStateJson = JsonSerializer.Serialize(new
            {
                EmpCode = cleanCode,
                ShiftCode = shift.ShiftCode,
                ShiftName = shift.ShiftName,
                RosterDate = targetDate.ToString("yyyy-MM-dd"),
                AssignedBy = dto.AssignedBy
            });

            await _auditService.LogSecurityActionAsync(
                "ROSTER_ASSIGNED",
                $"{cleanCode} ({shift.ShiftCode})",
                $"Assigned {shift.ShiftCode} ({shift.ShiftName}) to worker {cleanCode} ({employee.FullName}) for {targetDate:yyyy-MM-dd}.",
                performedByUserId,
                performedByUsername ?? dto.AssignedBy ?? "System",
                performedByOfficerName ?? dto.AssignedBy ?? "System",
                performedByRole ?? "Officer",
                oldStateJson,
                newStateJson
            );
        }

        return true;
    }

    public async Task<int> BulkAssignRosterAsync(
        List<AssignRosterDto> dtoList,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        if (dtoList == null || !dtoList.Any()) return 0;

        int count = 0;
        foreach (var item in dtoList)
        {
            bool ok = await AssignShiftAsync(item, performedByUserId, performedByUsername, performedByOfficerName, performedByRole, skipAudit: true);
            if (ok) count++;
        }

        var batchSummary = new
        {
            TotalRows = dtoList.Count,
            SuccessCount = count,
            FailureCount = dtoList.Count - count,
            DatesCovered = dtoList.Select(d => d.RosterDate.ToString("yyyy-MM-dd")).Distinct().Take(5).ToList()
        };

        await _auditService.LogSecurityActionAsync(
            "BULK_ROSTER_IMPORTED",
            $"Batch ({count}/{dtoList.Count} assigned)",
            $"Bulk imported shift rosters: {count} worker schedules assigned successfully ({dtoList.Count - count} skipped).",
            performedByUserId,
            performedByUsername ?? "System",
            performedByOfficerName ?? "Bulk System",
            performedByRole ?? "Officer",
            null,
            JsonSerializer.Serialize(batchSummary)
        );

        return count;
    }

    public async Task<EmployeeShiftRosterDto?> GetEmployeeShiftForDateAsync(string empCode, DateTime date)
    {
        string cleanCode = empCode.Trim().ToUpper();
        var targetDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var r = await _context.EmployeeShiftRosters
            .AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Shift)
            .FirstOrDefaultAsync(x => x.EmpCode.ToUpper() == cleanCode && x.RosterDate == targetDate);

        if (r == null) return null;

        return new EmployeeShiftRosterDto(
            r.Id,
            r.EmployeeId,
            r.EmpCode,
            r.Employee != null ? r.Employee.FullName : r.EmpCode,
            r.Employee?.SubArea?.SubAreaName ?? "—",
            r.RosterDate,
            r.ShiftId,
            r.ShiftCode,
            r.Shift?.ShiftName ?? r.ShiftCode,
            r.AssignedBy,
            r.CreatedAt
        );
    }
}

