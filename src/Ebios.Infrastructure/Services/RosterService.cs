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
    private readonly ILogger<RosterService> _logger;

    public RosterService(AppDbContext context, ILogger<RosterService> logger)
    {
        _context = context;
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

    public async Task<bool> AssignShiftAsync(AssignRosterDto dto)
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

        if (existing != null)
        {
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

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<int> BulkAssignRosterAsync(List<AssignRosterDto> dtoList)
    {
        if (dtoList == null || !dtoList.Any()) return 0;

        int count = 0;
        foreach (var item in dtoList)
        {
            bool ok = await AssignShiftAsync(item);
            if (ok) count++;
        }
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

