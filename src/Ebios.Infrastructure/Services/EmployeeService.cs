using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class EmployeeService : IEmployeeService
{
    private readonly AppDbContext _context;
    private readonly IEbiosSoapService _ebiosSoapService;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeService(AppDbContext context, IEbiosSoapService ebiosSoapService, ILogger<EmployeeService> logger)
    {
        _context = context;
        _ebiosSoapService = ebiosSoapService;
        _logger = logger;
    }

    private static List<int> ParseAdditionalIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new List<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(s => int.TryParse(s, out int id) ? id : 0)
                  .Where(id => id > 0)
                  .Distinct()
                  .ToList();
    }

    private async Task<string> BuildLocationCodesAsync(int primarySubAreaId, string? additionalIdsCsv)
    {
        var allIds = new HashSet<int> { primarySubAreaId };
        foreach (var id in ParseAdditionalIds(additionalIdsCsv))
        {
            allIds.Add(id);
        }

        var subAreas = await _context.SubAreas
            .AsNoTracking()
            .Where(s => allIds.Contains(s.Id))
            .ToListAsync();

        var primary = subAreas.FirstOrDefault(s => s.Id == primarySubAreaId);
        var others = subAreas.Where(s => s.Id != primarySubAreaId).ToList();

        var codes = new List<string>();
        if (primary != null) codes.Add(primary.SubAreaCode);
        codes.AddRange(others.Select(o => o.SubAreaCode));

        return string.Join(",", codes);
    }

    public async Task<List<EmployeeDto>> GetEmployeesAsync(int? areaId = null, int? subAreaId = null)
    {
        var subAreaLookup = await _context.SubAreas
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.SubAreaName);

        var query = _context.Employees
            .AsNoTracking()
            .Include(e => e.SubArea)
            .ThenInclude(s => s!.Area)
            .AsQueryable();

        if (subAreaId.HasValue)
        {
            query = query.Where(e => e.SubAreaId == subAreaId.Value || 
                (e.AdditionalSubAreaIds != null && e.AdditionalSubAreaIds.Contains(subAreaId.Value.ToString())));
        }
        else if (areaId.HasValue)
        {
            query = query.Where(e => e.SubArea != null && e.SubArea.AreaId == areaId.Value);
        }

        var employees = await query
            .OrderBy(e => e.FullName)
            .ToListAsync();

        return employees.Select(e =>
        {
            var addIds = ParseAdditionalIds(e.AdditionalSubAreaIds);
            var addNames = addIds.Where(id => subAreaLookup.ContainsKey(id)).Select(id => subAreaLookup[id]).ToList();

            return new EmployeeDto(
                e.Id,
                e.EmpCode,
                e.FullName,
                e.Designation,
                e.SubAreaId,
                e.SubArea != null ? e.SubArea.SubAreaName : string.Empty,
                e.SubArea != null && e.SubArea.Area != null ? e.SubArea.Area.AreaName : string.Empty,
                addIds,
                addNames,
                e.IsActive,
                e.IsSyncedToEbios,
                e.LastSyncedAt,
                e.CreatedAt
            );
        }).ToList();
    }

    public async Task<EmployeeDto?> GetEmployeeByIdAsync(int id)
    {
        var e = await _context.Employees
            .AsNoTracking()
            .Include(x => x.SubArea)
            .ThenInclude(s => s!.Area)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (e == null) return null;

        var subAreaLookup = await _context.SubAreas
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.SubAreaName);

        var addIds = ParseAdditionalIds(e.AdditionalSubAreaIds);
        var addNames = addIds.Where(subId => subAreaLookup.ContainsKey(subId)).Select(subId => subAreaLookup[subId]).ToList();

        return new EmployeeDto(
            e.Id,
            e.EmpCode,
            e.FullName,
            e.Designation,
            e.SubAreaId,
            e.SubArea != null ? e.SubArea.SubAreaName : string.Empty,
            e.SubArea != null && e.SubArea.Area != null ? e.SubArea.Area.AreaName : string.Empty,
            addIds,
            addNames,
            e.IsActive,
            e.IsSyncedToEbios,
            e.LastSyncedAt,
            e.CreatedAt
        );
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeDto dto)
    {
        string normalizedCode = dto.EmpCode.Trim().ToUpperInvariant();
        if (await _context.Employees.AnyAsync(e => e.EmpCode == normalizedCode))
        {
            throw new InvalidOperationException($"Worker with Employee Code '{normalizedCode}' already exists.");
        }

        var subArea = await _context.SubAreas.Include(s => s.Area).FirstOrDefaultAsync(s => s.Id == dto.SubAreaId);
        if (subArea == null)
        {
            throw new InvalidOperationException($"Colliery Sub-Area with ID {dto.SubAreaId} was not found.");
        }

        var additionalIds = dto.AdditionalSubAreaIds?.Where(id => id != dto.SubAreaId).Distinct().ToList() ?? new List<int>();
        string? additionalCsv = additionalIds.Any() ? string.Join(",", additionalIds) : null;

        var employee = new Employee
        {
            EmpCode = normalizedCode,
            FullName = dto.FullName.Trim(),
            Designation = string.IsNullOrWhiteSpace(dto.Designation) ? "General Mazdoor" : dto.Designation.Trim(),
            SubAreaId = dto.SubAreaId,
            AdditionalSubAreaIds = additionalCsv,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        // Retroactively link any past flagged punches and muster roll summaries for this employee code
        try
        {
            var pastFlaggedPunches = await _context.AttendanceLogs
                .Where(l => l.EmpCode.ToUpper() == normalizedCode && (l.EmployeeId == null || l.IsFlagged))
                .ToListAsync();

            foreach (var p in pastFlaggedPunches)
            {
                p.EmployeeId = employee.Id;
                p.Status = "Verified";
                p.IsFlagged = false;
            }

            var pastSummaries = await _context.AttendanceDailySummaries
                .Where(s => s.EmpCode.ToUpper() == normalizedCode && (s.EmployeeId == null || s.IsFlagged))
                .ToListAsync();

            foreach (var s in pastSummaries)
            {
                s.EmployeeId = employee.Id;
                s.EmployeeName = employee.FullName;
                s.IsFlagged = false;
                s.IsVerified = true;
                if (s.Remarks != null && s.Remarks.Contains("Flagged"))
                {
                    s.Remarks = s.RosterStatus == "Auto-Matched" ? $"Auto-matched to {s.ShiftCode}" : null;
                }
            }

            if (pastFlaggedPunches.Any() || pastSummaries.Any())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Retroactively linked {PunchCount} flagged punches and {SummaryCount} muster summaries to new employee {Code}.", pastFlaggedPunches.Count, pastSummaries.Count, employee.EmpCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retroactively link flagged punches for {Code}.", employee.EmpCode);
        }

        // Build comma-separated location codes (single or multiple) as per eBios contract
        string locationCodes = await BuildLocationCodesAsync(employee.SubAreaId, employee.AdditionalSubAreaIds);

        // Background auto-sync to eBios SOAP Server
        try
        {
            var (success, msg) = await _ebiosSoapService.UpdateEmployeeAsync(
                employee.EmpCode,
                employee.FullName,
                locationCodes,
                "Employee",
                "None"
            );

            if (success)
            {
                employee.IsSyncedToEbios = true;
                employee.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully synced Employee {Code} to eBios locations: {Locations}", employee.EmpCode, locationCodes);
            }
            else
            {
                _logger.LogWarning("eBios UpdateEmployee returned non-success for {Code}: {Msg}", employee.EmpCode, msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-sync Employee {Code} to eBios server.", employee.EmpCode);
        }

        var subAreaLookup = await _context.SubAreas.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.SubAreaName);
        var addNames = additionalIds.Where(id => subAreaLookup.ContainsKey(id)).Select(id => subAreaLookup[id]).ToList();

        return new EmployeeDto(
            employee.Id,
            employee.EmpCode,
            employee.FullName,
            employee.Designation,
            employee.SubAreaId,
            subArea.SubAreaName,
            subArea.Area != null ? subArea.Area.AreaName : string.Empty,
            additionalIds,
            addNames,
            employee.IsActive,
            employee.IsSyncedToEbios,
            employee.LastSyncedAt,
            employee.CreatedAt
        );
    }

    public async Task<(bool success, string message)> UpdateEmployeeAsync(int id, UpdateEmployeeDto dto, int? requestingUserAreaId = null)
    {
        var employee = await _context.Employees
            .Include(e => e.SubArea)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            return (false, "Employee not found.");
        }

        var newSubArea = await _context.SubAreas
            .Include(s => s.Area)
            .FirstOrDefaultAsync(s => s.Id == dto.SubAreaId);

        if (newSubArea == null)
        {
            return (false, "Target Colliery Unit was not found.");
        }

        // Permission check for Area Personnel Managers (APMs)
        if (requestingUserAreaId.HasValue)
        {
            if (employee.SubArea != null && employee.SubArea.AreaId != requestingUserAreaId.Value)
            {
                return (false, "Permission denied: You do not have permission to modify workers outside your assigned Mining Area.");
            }

            if (newSubArea.AreaId != requestingUserAreaId.Value)
            {
                return (false, "Permission denied: As an Area Personnel Manager, you can only assign units within your assigned Mining Area.");
            }
        }

        var additionalIds = dto.AdditionalSubAreaIds?.Where(subId => subId != dto.SubAreaId).Distinct().ToList() ?? new List<int>();

        employee.FullName = dto.FullName.Trim();
        employee.Designation = string.IsNullOrWhiteSpace(dto.Designation) ? "General Mazdoor" : dto.Designation.Trim();
        employee.SubAreaId = dto.SubAreaId;
        employee.AdditionalSubAreaIds = additionalIds.Any() ? string.Join(",", additionalIds) : null;

        await _context.SaveChangesAsync();

        // Build comma-separated location codes (single or multiple) as per eBios contract
        string locationCodes = await BuildLocationCodesAsync(employee.SubAreaId, employee.AdditionalSubAreaIds);

        // Dispatch background update to eBios biometric server
        try
        {
            var (soapSuccess, soapMsg) = await _ebiosSoapService.UpdateEmployeeAsync(
                employee.EmpCode,
                employee.FullName,
                locationCodes,
                "Employee",
                "None"
            );

            if (soapSuccess)
            {
                employee.IsSyncedToEbios = true;
                employee.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully updated and re-synced Employee {Code} to locations: {Locations}", employee.EmpCode, locationCodes);
                return (true, $"Worker '{employee.FullName}' updated with locations [{locationCodes}]. Biometric turnstiles updated.");
            }
            else
            {
                _logger.LogWarning("eBios UpdateEmployee returned non-success for {Code}: {Msg}", employee.EmpCode, soapMsg);
                return (true, $"Worker updated locally, but biometric server returned: {soapMsg}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch SOAP update for employee {Code}", employee.EmpCode);
            return (true, $"Worker updated in database, but biometric sync encountered an issue: {ex.Message}");
        }
    }

    public async Task<bool> ToggleEmployeeStatusAsync(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return false;

        employee.IsActive = !employee.IsActive;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Toggled active status for Employee {Code} to {Status}", employee.EmpCode, employee.IsActive);
        return true;
    }

    public async Task<bool> SyncEmployeeAsync(int id)
    {
        var employee = await _context.Employees.Include(e => e.SubArea).FirstOrDefaultAsync(e => e.Id == id);
        if (employee == null || employee.SubArea == null) return false;

        try
        {
            string locationCodes = await BuildLocationCodesAsync(employee.SubAreaId, employee.AdditionalSubAreaIds);

            var (success, msg) = await _ebiosSoapService.UpdateEmployeeAsync(
                employee.EmpCode,
                employee.FullName,
                locationCodes,
                "Employee",
                "None"
            );

            if (success)
            {
                employee.IsSyncedToEbios = true;
                employee.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return true;
            }

            _logger.LogWarning("Re-sync for employee {Code} failed: {Msg}", employee.EmpCode, msg);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while re-syncing employee {Code}", employee.EmpCode);
            return false;
        }
    }
}
