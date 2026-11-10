using System.Text.Json;
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
    private readonly IAuditService _auditService;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeService(
        AppDbContext context, 
        IEbiosSoapService ebiosSoapService, 
        IAuditService auditService,
        ILogger<EmployeeService> logger)
    {
        _context = context;
        _ebiosSoapService = ebiosSoapService;
        _auditService = auditService;
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
                e.SubArea != null ? e.SubArea.SubAreaName : "Unassigned (No Location Added)",
                e.SubArea != null && e.SubArea.Area != null ? e.SubArea.Area.AreaName : "—",
                addIds,
                addNames,
                e.IsActive,
                e.IsBlacklisted,
                e.IsHigherAuthority,
                e.StatusReason,
                e.BlockedDeviceSerials,
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
            e.SubArea != null ? e.SubArea.SubAreaName : "Unassigned (No Location Added)",
            e.SubArea != null && e.SubArea.Area != null ? e.SubArea.Area.AreaName : "—",
            addIds,
            addNames,
            e.IsActive,
            e.IsBlacklisted,
            e.IsHigherAuthority,
            e.StatusReason,
            e.BlockedDeviceSerials,
            e.IsSyncedToEbios,
            e.LastSyncedAt,
            e.CreatedAt
        );
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(
        CreateEmployeeDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        string normalizedCode = dto.EmpCode.Trim().ToUpperInvariant();
        if (dto.SubAreaId <= 0)
        {
            throw new InvalidOperationException("Assigned Colliery Unit is compulsory. Please select a valid Colliery Unit.");
        }

        var subArea = await _context.SubAreas.Include(s => s.Area).FirstOrDefaultAsync(s => s.Id == dto.SubAreaId);
        if (subArea == null)
        {
            throw new InvalidOperationException($"Colliery Sub-Area with ID {dto.SubAreaId} was not found.");
        }

        var additionalIds = dto.AdditionalSubAreaIds?.Where(id => id != dto.SubAreaId).Distinct().ToList() ?? new List<int>();
        string? additionalCsv = additionalIds.Any() ? string.Join(",", additionalIds) : null;

        var existingEmp = await _context.Employees.Include(e => e.SubArea).ThenInclude(s => s!.Area).FirstOrDefaultAsync(e => e.EmpCode == normalizedCode);
        Employee employee;

        if (existingEmp != null)
        {
            // Employee already exists: update details seamlessly (upsert)
            existingEmp.FullName = dto.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Designation)) existingEmp.Designation = dto.Designation.Trim();
            existingEmp.SubAreaId = dto.SubAreaId;
            existingEmp.AdditionalSubAreaIds = additionalCsv;
            existingEmp.IsHigherAuthority = dto.IsHigherAuthority;
            existingEmp.IsActive = true;
            employee = existingEmp;
        }
        else
        {
            employee = new Employee
            {
                EmpCode = normalizedCode,
                FullName = dto.FullName.Trim(),
                Designation = string.IsNullOrWhiteSpace(dto.Designation) ? "Colliery Worker" : dto.Designation.Trim(),
                SubAreaId = dto.SubAreaId,
                AdditionalSubAreaIds = additionalCsv,
                IsHigherAuthority = dto.IsHigherAuthority,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Employees.Add(employee);
        }

        await _context.SaveChangesAsync();

        var newState = new
        {
            employee.Id,
            employee.EmpCode,
            employee.FullName,
            employee.Designation,
            employee.SubAreaId,
            SubAreaName = subArea.SubAreaName,
            AreaName = subArea.Area?.AreaName,
            Category = employee.IsHigherAuthority ? "Executive" : "Non-Executive",
            IsHigherAuthority = employee.IsHigherAuthority
        };

        await _auditService.LogSecurityActionAsync(
            "WORKER_CREATED",
            $"EMP:{employee.EmpCode}",
            $"Worker '{employee.FullName}' ({employee.EmpCode}) onboarded to unit [{subArea.SubAreaName}] by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            null,
            JsonSerializer.Serialize(newState));

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

        if (!string.IsNullOrWhiteSpace(locationCodes))
        {
            // Background auto-sync to eBios SOAP Server (only when location is assigned)
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
            employee.IsBlacklisted,
            employee.IsHigherAuthority,
            employee.StatusReason,
            employee.BlockedDeviceSerials,
            employee.IsSyncedToEbios,
            employee.LastSyncedAt,
            employee.CreatedAt
        );
    }

    public async Task<(bool success, string message)> UpdateEmployeeAsync(
        int id, 
        UpdateEmployeeDto dto, 
        int? requestingUserAreaId = null,
        int? requestingUserSubAreaId = null,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var employee = await _context.Employees
            .Include(e => e.SubArea)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
        {
            return (false, "Employee not found.");
        }

        if (dto.SubAreaId <= 0)
        {
            return (false, "Colliery Unit is required. Please select a valid Colliery Unit.");
        }

        var newSubArea = await _context.SubAreas
            .Include(s => s.Area)
            .FirstOrDefaultAsync(s => s.Id == dto.SubAreaId);

        if (newSubArea == null)
        {
            return (false, "Target Colliery Unit was not found.");
        }

        // Permission check for Unit Personnel Managers (UPMs) - Strictly locked to their pit
        if (requestingUserSubAreaId.HasValue && requestingUserSubAreaId.Value > 0)
        {
            if (employee.SubAreaId != requestingUserSubAreaId.Value)
            {
                return (false, "Permission denied: You do not have permission to modify workers outside your assigned Colliery Unit.");
            }

            if (newSubArea.Id != requestingUserSubAreaId.Value)
            {
                return (false, "Permission denied: As a Unit Personnel Manager, you cannot transfer workers outside your assigned Colliery Unit.");
            }
        }
        // Permission check for Area Personnel Managers (APMs)
        else if (requestingUserAreaId.HasValue)
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

        bool oldIsHigherAuthority = employee.IsHigherAuthority;
        string oldFullName = employee.FullName;
        string oldDesignation = employee.Designation;
        int oldSubAreaId = employee.SubAreaId;
        string? oldAdditionalIds = employee.AdditionalSubAreaIds;
        string oldCategory = oldIsHigherAuthority ? "Executive" : "Non-Executive";
        string newCategory = dto.IsHigherAuthority ? "Executive" : "Non-Executive";

        var additionalIds = dto.AdditionalSubAreaIds?.Where(subId => subId != dto.SubAreaId).Distinct().ToList() ?? new List<int>();
        string? newAdditionalCsv = additionalIds.Any() ? string.Join(",", additionalIds) : null;
        string newFullName = dto.FullName.Trim();
        string newDesignation = string.IsNullOrWhiteSpace(dto.Designation) ? "Colliery Worker" : dto.Designation.Trim();

        bool locationsChanged = (oldSubAreaId != dto.SubAreaId) || !string.Equals(oldAdditionalIds ?? "", newAdditionalCsv ?? "", StringComparison.Ordinal);
        bool nameChanged = !string.Equals(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase);
        bool authorityChanged = (oldIsHigherAuthority != dto.IsHigherAuthority);
        bool designationChanged = !string.Equals(oldDesignation, newDesignation, StringComparison.OrdinalIgnoreCase);

        // Case 1: Colliery Unit location or Worker Name changed (requires updating biometric server / physical turnstiles)
        if (locationsChanged || nameChanged)
        {
            // Build comma-separated location codes (single or multiple) as per eBios contract
            string locationCodes = await BuildLocationCodesAsync(dto.SubAreaId, newAdditionalCsv);

            try
            {
                var (soapSuccess, soapMsg) = await _ebiosSoapService.UpdateEmployeeAsync(
                    employee.EmpCode,
                    newFullName,
                    locationCodes,
                    "Employee",
                    "None"
                );

                if (!soapSuccess)
                {
                    _logger.LogWarning("eBios UpdateEmployee returned non-success for {Code}: {Msg}", employee.EmpCode, soapMsg);
                    return (false, $"Biometric turnstile sync failed: {soapMsg}. Worker record was not modified.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch SOAP update for employee {Code}", employee.EmpCode);
                return (false, "Cannot connect to Biometric Server. Worker record was not modified.");
            }
        }

        // Apply changes to database entity now that third-party verification succeeded (or was not required)
        var previousState = new
        {
            employee.Id,
            employee.EmpCode,
            FullName = oldFullName,
            Designation = oldDesignation,
            SubAreaId = oldSubAreaId,
            SubAreaName = employee.SubArea?.SubAreaName,
            AdditionalSubAreaIds = oldAdditionalIds,
            Category = oldCategory,
            IsHigherAuthority = oldIsHigherAuthority
        };

        employee.FullName = newFullName;
        employee.Designation = newDesignation;
        employee.SubAreaId = dto.SubAreaId;
        employee.IsHigherAuthority = dto.IsHigherAuthority;
        employee.AdditionalSubAreaIds = newAdditionalCsv;

        if (locationsChanged || nameChanged)
        {
            employee.IsSyncedToEbios = true;
            employee.LastSyncedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        var newState = new
        {
            employee.Id,
            employee.EmpCode,
            FullName = employee.FullName,
            Designation = employee.Designation,
            SubAreaId = employee.SubAreaId,
            SubAreaName = newSubArea.SubAreaName,
            AdditionalSubAreaIds = employee.AdditionalSubAreaIds,
            Category = newCategory,
            IsHigherAuthority = employee.IsHigherAuthority
        };

        // Audit logging: Executive <-> Non-Executive category change
        if (authorityChanged)
        {
            await _auditService.LogSecurityActionAsync(
                "EMPLOYEE_CATEGORY_CHANGED",
                $"EMP:{employee.EmpCode}",
                $"Worker '{employee.FullName}' ({employee.EmpCode}) category changed from {oldCategory} to {newCategory} (Single-punch privilege {(dto.IsHigherAuthority ? "GRANTED" : "REVOKED")}) by {performedByUsername ?? "Admin"}.",
                performedByUserId,
                performedByUsername ?? "admin",
                performedByOfficerName ?? performedByUsername ?? "System Admin",
                performedByRole ?? "Officer",
                JsonSerializer.Serialize(previousState),
                JsonSerializer.Serialize(newState));
        }

        // Audit logging: Colliery Unit transfer
        if (locationsChanged)
        {
            await _auditService.LogSecurityActionAsync(
                "WORKER_TRANSFER",
                $"EMP:{employee.EmpCode}",
                $"Worker '{employee.FullName}' ({employee.EmpCode}) location updated from [{employee.SubArea?.SubAreaName}] to [{newSubArea.SubAreaName}] by {performedByUsername ?? "Admin"}.",
                performedByUserId,
                performedByUsername ?? "admin",
                performedByOfficerName ?? performedByUsername ?? "System Admin",
                performedByRole ?? "Officer",
                JsonSerializer.Serialize(previousState),
                JsonSerializer.Serialize(newState));
        }

        if (locationsChanged || nameChanged)
        {
            return (true, $"Worker '{employee.FullName}' updated and synchronized with biometric turnstiles.");
        }

        return (true, $"Worker '{employee.FullName}' ({employee.EmpCode}) updated successfully.");
    }

    public async Task<List<WorkerDeviceAccessDto>> GetWorkerDevicesAsync(int employeeId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return new List<WorkerDeviceAccessDto>();

        var targetSubAreaIds = new List<int>();
        if (employee.SubAreaId > 0)
        {
            targetSubAreaIds.Add(employee.SubAreaId);
        }
        if (!string.IsNullOrWhiteSpace(employee.AdditionalSubAreaIds))
        {
            var addIds = employee.AdditionalSubAreaIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var parsed) ? parsed : 0)
                .Where(id => id > 0);
            targetSubAreaIds.AddRange(addIds);
        }

        var blockedSerials = (employee.BlockedDeviceSerials ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .ToHashSet();

        var devices = await _context.Devices
            .Include(d => d.SubArea)
            .Where(d => targetSubAreaIds.Contains(d.SubAreaId))
            .OrderBy(d => d.DeviceName)
            .ToListAsync();

        return devices.Select(d => new WorkerDeviceAccessDto(
            d.Id,
            d.SerialNumber,
            d.DeviceName,
            d.SubArea?.SubAreaName ?? "Colliery Unit",
            d.Direction,
            blockedSerials.Contains(d.SerialNumber.ToUpperInvariant()) || !employee.IsActive
        )).ToList();
    }

    public async Task<(bool success, string message)> ToggleWorkerDeviceAccessAsync(
        int employeeId, 
        string deviceSerialNumber, 
        bool block,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return (false, "Employee not found.");

        string serial = deviceSerialNumber.Trim().ToUpperInvariant();

        // 1. Send hardware SOAP command to the physical turnstile
        var (soapSuccess, soapMsg) = await _ebiosSoapService.BlockUnblockUserOnDeviceAsync(
            deviceSerialNumber,
            employee.EmpCode,
            blockUser: block
        );

        if (!soapSuccess)
        {
            _logger.LogWarning("Turnstile block/unblock failed for {Serial} / {Code}: {Msg}", deviceSerialNumber, employee.EmpCode, soapMsg);
            return (false, $"Turnstile command failed: {soapMsg}. Hardware access was not changed.");
        }

        // 2. Only if hardware confirmed success, update employee's BlockedDeviceSerials set
        var blockedSet = (employee.BlockedDeviceSerials ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .ToHashSet();

        if (block)
        {
            blockedSet.Add(serial);
        }
        else
        {
            blockedSet.Remove(serial);
        }

        employee.BlockedDeviceSerials = blockedSet.Any() ? string.Join(",", blockedSet) : null;

        // Check if all devices are blocked
        var allDevices = await GetWorkerDevicesAsync(employeeId);
        int totalDevs = allDevices.Count;
        int blockedCount = blockedSet.Count;

        if (totalDevs > 0 && blockedCount >= totalDevs)
        {
            employee.IsActive = false;
        }
        else
        {
            employee.IsActive = true;
        }

        await _context.SaveChangesAsync();

        string action = block ? "Blocked" : "Unblocked";
        string msg = $"Turnstile {deviceSerialNumber} {action.ToLower()} for worker {employee.FullName} ({employee.EmpCode}).";

        await _auditService.LogSecurityActionAsync(
            block ? "WORKER_DEVICE_BLOCKED" : "WORKER_DEVICE_UNBLOCKED",
            $"EMP:{employee.EmpCode} | DEV:{deviceSerialNumber}",
            $"Worker '{employee.FullName}' ({employee.EmpCode}) access on turnstile {deviceSerialNumber} was {action.ToLower()} by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            null,
            JsonSerializer.Serialize(new { employee.EmpCode, deviceSerialNumber, blocked = block }));

        return (true, msg);
    }

    public async Task<(bool success, string message)> BlockAllDevicesForWorkerAsync(
        int employeeId, 
        bool blockAll,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return (false, "Employee not found.");

        bool oldIsActive = employee.IsActive;

        var devices = await GetWorkerDevicesAsync(employeeId);
        if (!devices.Any())
        {
            employee.IsActive = !blockAll;
            employee.BlockedDeviceSerials = null;
            await _context.SaveChangesAsync();
            return (true, $"Worker status updated. (No active biometric turnstiles registered in this worker's unit).");
        }

        int successCount = 0;
        foreach (var dev in devices)
        {
            try
            {
                var (success, _) = await _ebiosSoapService.BlockUnblockUserOnDeviceAsync(
                    dev.SerialNumber,
                    employee.EmpCode,
                    blockUser: blockAll
                );
                if (success) successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send block/unblock command to {Serial} for {Code}", dev.SerialNumber, employee.EmpCode);
            }
        }

        // If any hardware turnstile command failed, fail and do NOT alter local status
        if (successCount < devices.Count && devices.Any())
        {
            return (false, $"Unable to communicate with all biometric turnstiles ({successCount}/{devices.Count} turnstiles responded). Hardware access status was not changed.");
        }

        if (blockAll)
        {
            employee.IsActive = false;
            employee.BlockedDeviceSerials = string.Join(",", devices.Select(d => d.SerialNumber));
        }
        else
        {
            employee.IsActive = true;
            employee.BlockedDeviceSerials = null;
        }

        await _context.SaveChangesAsync();

        string action = blockAll ? "Blocked" : "Unblocked";

        var previousState = new { employee.Id, employee.EmpCode, employee.FullName, IsActive = oldIsActive };
        var newState = new { employee.Id, employee.EmpCode, employee.FullName, IsActive = employee.IsActive };

        await _auditService.LogSecurityActionAsync(
            blockAll ? "WORKER_ACCESS_BLOCKED" : "WORKER_ACCESS_UNBLOCKED",
            $"EMP:{employee.EmpCode}",
            $"Worker '{employee.FullName}' ({employee.EmpCode}) was {action.ToLower()} across all turnstiles by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            JsonSerializer.Serialize(previousState),
            JsonSerializer.Serialize(newState));

        return (true, $"{action} worker '{employee.FullName}' across all {devices.Count} turnstiles ({successCount}/{devices.Count} device commands executed).");
    }

    public async Task<(bool success, string message)> ToggleEmployeeStatusAsync(
        int id, 
        string? reason = null, 
        bool isBlacklist = false,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return (false, "Employee not found.");

        bool targetBlock = employee.IsActive; // If currently active, we want to block on all devices
        return await BlockAllDevicesForWorkerAsync(id, targetBlock, performedByUserId, performedByUsername, performedByOfficerName, performedByRole);
    }

    public async Task<(bool success, string message)> ToggleHigherAuthorityAsync(
        int employeeId, 
        int? requestingUserAreaId = null,
        int? requestingUserSubAreaId = null,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var employee = await _context.Employees
            .Include(e => e.SubArea)
            .FirstOrDefaultAsync(e => e.Id == employeeId);

        if (employee == null) return (false, "Employee not found.");

        // Unit Personnel Manager jurisdiction check
        if (requestingUserSubAreaId.HasValue && requestingUserSubAreaId.Value > 0)
        {
            if (employee.SubAreaId != requestingUserSubAreaId.Value)
            {
                return (false, "Permission denied. You can only configure Higher Authorities for workers in your own assigned Colliery Unit.");
            }
        }
        // Area Personnel Manager jurisdiction check
        else if (requestingUserAreaId.HasValue && requestingUserAreaId.Value > 0)
        {
            if (employee.SubArea == null || employee.SubArea.AreaId != requestingUserAreaId.Value)
            {
                return (false, "Permission denied. You can only map Higher Authorities in your own assigned Mining Area.");
            }
        }

        bool oldIsHigherAuthority = employee.IsHigherAuthority;
        string oldRole = oldIsHigherAuthority ? "Executive" : "Non-Executive";

        employee.IsHigherAuthority = !employee.IsHigherAuthority;
        await _context.SaveChangesAsync();

        string newRole = employee.IsHigherAuthority ? "Executive" : "Non-Executive";
        _logger.LogInformation("Updated employee {Code} authority status to: {Status}", employee.EmpCode, newRole);

        var previousState = new { employee.Id, employee.EmpCode, employee.FullName, Category = oldRole, IsHigherAuthority = oldIsHigherAuthority };
        var newState = new { employee.Id, employee.EmpCode, employee.FullName, Category = newRole, IsHigherAuthority = employee.IsHigherAuthority };

        await _auditService.LogSecurityActionAsync(
            "EMPLOYEE_CATEGORY_CHANGED",
            $"EMP:{employee.EmpCode}",
            $"Worker '{employee.FullName}' ({employee.EmpCode}) category changed from {oldRole} to {newRole} (Single-punch privilege {(employee.IsHigherAuthority ? "GRANTED" : "REVOKED")}) by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            JsonSerializer.Serialize(previousState),
            JsonSerializer.Serialize(newState));

        return (true, $"{employee.FullName} ({employee.EmpCode}) is now mapped as {newRole}.");
    }

    public async Task<(int successCount, int totalCount, string message)> BulkBlacklistEmployeesAsync(
        List<BulkBlockEntryDto> entries, 
        string reason, 
        int? requestingUserAreaId = null, 
        int? requestingUserSubAreaId = null,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        if (entries == null || !entries.Any())
        {
            return (0, 0, "No employee codes provided.");
        }

        // Clean & group entries by normalized EmpCode
        var validEntries = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.EmpCode))
            .Select(e => new BulkBlockEntryDto(
                e.EmpCode.Trim().ToUpperInvariant(), 
                string.IsNullOrWhiteSpace(e.DeviceSerialNumber) ? null : e.DeviceSerialNumber.Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(e.Action) ? "Block" : e.Action.Trim()
            ))
            .ToList();

        if (!validEntries.Any())
        {
            return (0, 0, "No valid employee codes found in input.");
        }

        var normalizedCodes = validEntries.Select(e => e.EmpCode).Distinct().ToList();

        var query = _context.Employees
            .Include(e => e.SubArea)
            .Where(e => normalizedCodes.Contains(e.EmpCode.ToUpper()));

        // Role-based jurisdictional check
        if (requestingUserSubAreaId.HasValue && requestingUserSubAreaId.Value > 0)
        {
            query = query.Where(e => e.SubAreaId == requestingUserSubAreaId.Value);
        }
        else if (requestingUserAreaId.HasValue && requestingUserAreaId.Value > 0)
        {
            query = query.Where(e => e.SubArea != null && e.SubArea.AreaId == requestingUserAreaId.Value);
        }

        var matchingEmployees = await query.ToListAsync();
        var matchedCodeSet = matchingEmployees.Select(e => e.EmpCode.ToUpperInvariant()).ToHashSet();
        var notFoundCodes = normalizedCodes.Where(c => !matchedCodeSet.Contains(c.ToUpperInvariant())).ToList();

        if (!matchingEmployees.Any())
        {
            string notFoundDetails = notFoundCodes.Any() ? $" Worker codes not found in database: [{string.Join(", ", notFoundCodes)}]." : "";
            return (0, validEntries.Count, $"No matching employees found within your permitted jurisdiction.{notFoundDetails}");
        }

        bool isBlacklist = reason.Contains("Blacklist", StringComparison.OrdinalIgnoreCase);

        // Map subarea IDs to fetch all associated devices
        var allSubAreaIds = new HashSet<int>();
        foreach (var emp in matchingEmployees)
        {
            if (emp.SubAreaId > 0)
            {
                allSubAreaIds.Add(emp.SubAreaId);
            }
            if (!string.IsNullOrWhiteSpace(emp.AdditionalSubAreaIds))
            {
                var addIds = emp.AdditionalSubAreaIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var parsed) ? parsed : 0)
                    .Where(subId => subId > 0);
                foreach (var id in addIds) allSubAreaIds.Add(id);
            }
        }

        // Get devices for these subareas
        var devices = await _context.Devices
            .Where(d => allSubAreaIds.Contains(d.SubAreaId))
            .ToListAsync();

        var deviceLookup = devices.GroupBy(d => d.SubAreaId).ToDictionary(g => g.Key, g => g.ToList());

        // Group requested entries per employee
        var entriesByEmp = validEntries
            .GroupBy(e => e.EmpCode)
            .ToDictionary(g => g.Key, g => g.ToList());

        int hardwareAttemptedCount = 0;
        int hardwareDispatchedCount = 0;
        int hardwareFailedCount = 0;
        var invalidDevices = new List<string>();
        int modifiedEmployeesCount = 0;

        foreach (var emp in matchingEmployees)
        {
            string empCodeUpper = emp.EmpCode.ToUpperInvariant();

            var empSubAreaIds = new List<int>();
            if (emp.SubAreaId > 0)
            {
                empSubAreaIds.Add(emp.SubAreaId);
            }
            if (!string.IsNullOrWhiteSpace(emp.AdditionalSubAreaIds))
            {
                empSubAreaIds.AddRange(emp.AdditionalSubAreaIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var parsed) ? parsed : 0)
                    .Where(id => id > 0));
            }

            var empAllDevices = empSubAreaIds
                .Where(id => deviceLookup.ContainsKey(id))
                .SelectMany(id => deviceLookup[id])
                .DistinctBy(d => d.SerialNumber.ToUpperInvariant())
                .ToList();

            var currentBlocked = (emp.BlockedDeviceSerials ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .ToHashSet();

            var empEntries = entriesByEmp.ContainsKey(empCodeUpper) ? entriesByEmp[empCodeUpper] : new List<BulkBlockEntryDto>();
            bool empModified = false;

            foreach (var item in empEntries)
            {
                bool isUnblock = item.Action.Equals("Unblock", StringComparison.OrdinalIgnoreCase) 
                              || item.Action.Equals("Unlock", StringComparison.OrdinalIgnoreCase) 
                              || item.Action == "0" 
                              || item.Action.Equals("Active", StringComparison.OrdinalIgnoreCase);

                bool isAllDevices = string.IsNullOrWhiteSpace(item.DeviceSerialNumber) || item.DeviceSerialNumber.Equals("ALL", StringComparison.OrdinalIgnoreCase);

                if (isUnblock)
                {
                    if (isAllDevices)
                    {
                        var successfulUnblocks = new List<string>();
                        foreach (var dev in empAllDevices)
                        {
                            hardwareAttemptedCount++;
                            try
                            {
                                var (success, _) = await _ebiosSoapService.BlockUnblockUserOnDeviceAsync(dev.SerialNumber, emp.EmpCode, blockUser: false);
                                if (success)
                                {
                                    hardwareDispatchedCount++;
                                    successfulUnblocks.Add(dev.SerialNumber.ToUpperInvariant());
                                }
                                else hardwareFailedCount++;
                            }
                            catch (Exception ex)
                            {
                                hardwareFailedCount++;
                                _logger.LogWarning(ex, "Unblock failed for {Code} on device {Serial}", emp.EmpCode, dev.SerialNumber);
                            }
                        }

                        // Only alter employee DB state if turnstile confirmed the command
                        if (successfulUnblocks.Any())
                        {
                            foreach (var s in successfulUnblocks) currentBlocked.Remove(s);
                            emp.BlockedDeviceSerials = currentBlocked.Any() ? string.Join(",", currentBlocked) : null;
                            if (!currentBlocked.Any())
                            {
                                emp.IsActive = true;
                                emp.IsBlacklisted = false;
                                emp.StatusReason = null;
                            }
                            empModified = true;
                        }
                    }
                    else
                    {
                        string rawTarget = item.DeviceSerialNumber!.Trim();
                        var targetDev = empAllDevices.FirstOrDefault(d => 
                            d.SerialNumber.Equals(rawTarget, StringComparison.OrdinalIgnoreCase) ||
                            d.DeviceName.Equals(rawTarget, StringComparison.OrdinalIgnoreCase));

                        if (targetDev == null)
                        {
                            invalidDevices.Add($"Worker {emp.EmpCode}: Device '{rawTarget}' does not exist or is not assigned to this worker's colliery.");
                            continue;
                        }

                        hardwareAttemptedCount++;
                        bool confirmed = false;
                        try
                        {
                            var (success, _) = await _ebiosSoapService.BlockUnblockUserOnDeviceAsync(targetDev.SerialNumber, emp.EmpCode, blockUser: false);
                            if (success)
                            {
                                hardwareDispatchedCount++;
                                confirmed = true;
                            }
                            else hardwareFailedCount++;
                        }
                        catch (Exception ex)
                        {
                            hardwareFailedCount++;
                            _logger.LogWarning(ex, "Unblock failed for {Code} on device {Serial}", emp.EmpCode, targetDev.SerialNumber);
                        }

                        if (confirmed)
                        {
                            currentBlocked.Remove(targetDev.SerialNumber.ToUpperInvariant());
                            emp.BlockedDeviceSerials = currentBlocked.Any() ? string.Join(",", currentBlocked) : null;
                            if (!currentBlocked.Any())
                            {
                                emp.IsActive = true;
                                emp.IsBlacklisted = false;
                                emp.StatusReason = null;
                            }
                            empModified = true;
                        }
                    }
                }
                else // Block / Lock
                {
                    if (isAllDevices)
                    {
                        var successfulBlocks = new List<string>();
                        foreach (var dev in empAllDevices)
                        {
                            hardwareAttemptedCount++;
                            try
                            {
                                var (success, _) = await _ebiosSoapService.BlockUnblockUserOnDeviceAsync(dev.SerialNumber, emp.EmpCode, blockUser: true);
                                if (success)
                                {
                                    hardwareDispatchedCount++;
                                    successfulBlocks.Add(dev.SerialNumber.ToUpperInvariant());
                                }
                                else hardwareFailedCount++;
                            }
                            catch (Exception ex)
                            {
                                hardwareFailedCount++;
                                _logger.LogWarning(ex, "Block failed for {Code} on device {Serial}", emp.EmpCode, dev.SerialNumber);
                            }
                        }

                        // Only alter employee DB state if turnstile confirmed the command
                        if (successfulBlocks.Any())
                        {
                            foreach (var s in successfulBlocks) currentBlocked.Add(s);
                            emp.BlockedDeviceSerials = currentBlocked.Any() ? string.Join(",", currentBlocked) : null;
                            emp.StatusReason = reason;
                            emp.IsBlacklisted = isBlacklist;
                            if (empAllDevices.Any() && currentBlocked.Count >= empAllDevices.Count)
                            {
                                emp.IsActive = false;
                            }
                            empModified = true;
                        }
                    }
                    else
                    {
                        string rawTarget = item.DeviceSerialNumber!.Trim();
                        var targetDev = empAllDevices.FirstOrDefault(d => 
                            d.SerialNumber.Equals(rawTarget, StringComparison.OrdinalIgnoreCase) ||
                            d.DeviceName.Equals(rawTarget, StringComparison.OrdinalIgnoreCase));

                        if (targetDev == null)
                        {
                            invalidDevices.Add($"Worker {emp.EmpCode}: Device '{rawTarget}' does not exist or is not assigned to this worker's colliery.");
                            continue;
                        }

                        hardwareAttemptedCount++;
                        bool confirmed = false;
                        try
                        {
                            var (success, _) = await _ebiosSoapService.BlockUnblockUserOnDeviceAsync(targetDev.SerialNumber, emp.EmpCode, blockUser: true);
                            if (success)
                            {
                                hardwareDispatchedCount++;
                                confirmed = true;
                            }
                            else hardwareFailedCount++;
                        }
                        catch (Exception ex)
                        {
                            hardwareFailedCount++;
                            _logger.LogWarning(ex, "Block failed for {Code} on device {Serial}", emp.EmpCode, targetDev.SerialNumber);
                        }

                        if (confirmed)
                        {
                            currentBlocked.Add(targetDev.SerialNumber.ToUpperInvariant());
                            emp.BlockedDeviceSerials = currentBlocked.Any() ? string.Join(",", currentBlocked) : null;
                            emp.StatusReason = reason;
                            emp.IsBlacklisted = isBlacklist;
                            if (empAllDevices.Any() && currentBlocked.Count >= empAllDevices.Count)
                            {
                                emp.IsActive = false;
                            }
                            empModified = true;
                        }
                    }
                }
            }

            if (empModified)
            {
                modifiedEmployeesCount++;
            }
        }

        // If turnstiles were targeted but ZERO commands succeeded (eBioServer offline or connection refused), FAIL!
        if (hardwareAttemptedCount > 0 && hardwareDispatchedCount == 0)
        {
            string failMsg = $"Operation Failed: Central Biometric Server (eBioServer) is offline or unreachable (0 of {hardwareAttemptedCount} turnstiles responded). No changes were made in the database, and workers are NOT blocked.";
            if (invalidDevices.Any()) failMsg += $" ⚠️ {string.Join(" | ", invalidDevices)}";
            if (notFoundCodes.Any()) failMsg += $" ⚠️ Nonexistent worker codes: [{string.Join(", ", notFoundCodes)}]";
            return (0, validEntries.Count, failMsg);
        }

        if (modifiedEmployeesCount > 0)
        {
            await _context.SaveChangesAsync();
        }

        var bulkSummary = new
        {
            Action = isBlacklist ? "Blacklist" : "Turnstile Access Update",
            RequestedTotal = validEntries.Count,
            ProcessedCount = modifiedEmployeesCount,
            NotFoundCount = notFoundCodes.Count,
            NotFoundWorkerCodes = notFoundCodes,
            InvalidDevices = invalidDevices,
            HardwareAttempted = hardwareAttemptedCount,
            HardwareCommandsDispatched = hardwareDispatchedCount,
            HardwareCommandsFailed = hardwareFailedCount,
            Reason = reason,
            SampleWorkerCodes = matchingEmployees.Take(15).Select(e => e.EmpCode).ToList()
        };

        var statusParts = new List<string>();
        if (modifiedEmployeesCount > 0)
        {
            statusParts.Add($"Successfully updated {modifiedEmployeesCount} worker(s) ({hardwareDispatchedCount} of {hardwareAttemptedCount} turnstile commands confirmed).");
        }
        else
        {
            statusParts.Add("No workers were updated.");
        }

        if (invalidDevices.Any())
        {
            statusParts.Add($"⚠️ Device validation error: {string.Join(" | ", invalidDevices)}");
        }

        if (notFoundCodes.Any())
        {
            statusParts.Add($"⚠️ {notFoundCodes.Count} worker(s) NOT FOUND in database: [{string.Join(", ", notFoundCodes)}].");
        }

        string msg = string.Join(" ", statusParts);

        await _auditService.LogSecurityActionAsync(
            isBlacklist ? "BULK_WORKERS_BLACKLISTED" : "BULK_TURNSTILE_ACCESS_UPDATED",
            $"BATCH:{modifiedEmployeesCount}_OF_{validEntries.Count}_WORKERS",
            $"Bulk {bulkSummary.Action}: {modifiedEmployeesCount} of {validEntries.Count} worker(s) updated. {msg} (Reason: {reason}) by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            null,
            JsonSerializer.Serialize(bulkSummary));

        return (modifiedEmployeesCount, validEntries.Count, msg);
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

    public async Task<List<string>> GetDesignationsAsync()
    {
        var dbDesignations = await _context.DesignationMasters
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Title)
            .Select(d => d.Title)
            .ToListAsync();

        if (dbDesignations.Any()) return dbDesignations;

        var fallback = await _context.Employees
            .AsNoTracking()
            .Select(e => e.Designation)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();

        return fallback.Any() ? fallback : new List<string> { "Colliery Worker", "Mining Personnel", "Mining Sirdar" };
    }

    public async Task<(bool success, string message)> AddDesignationAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (false, "Designation title cannot be empty.");

        string cleanTitle = title.Trim();
        var existing = await _context.DesignationMasters.FirstOrDefaultAsync(d => d.Title.ToUpper() == cleanTitle.ToUpper());
        if (existing != null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await _context.SaveChangesAsync();
                return (true, $"Designation '{cleanTitle}' reactivated successfully.");
            }
            return (false, $"Designation '{cleanTitle}' already exists.");
        }

        _context.DesignationMasters.Add(new DesignationMaster
        {
            Title = cleanTitle,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return (true, $"Designation '{cleanTitle}' added successfully.");
    }

    public async Task<(bool success, string message)> UpdateDesignationAsync(string oldTitle, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(oldTitle) || string.IsNullOrWhiteSpace(newTitle))
            return (false, "Designation titles cannot be empty.");

        string cleanOld = oldTitle.Trim();
        string cleanNew = newTitle.Trim();

        var existing = await _context.DesignationMasters.FirstOrDefaultAsync(d => d.Title.ToUpper() == cleanOld.ToUpper());
        if (existing != null)
        {
            existing.Title = cleanNew;
        }
        else
        {
            _context.DesignationMasters.Add(new DesignationMaster { Title = cleanNew, IsActive = true });
        }

        var affectedEmployees = await _context.Employees
            .Where(e => e.Designation.ToUpper() == cleanOld.ToUpper())
            .ToListAsync();

        foreach (var emp in affectedEmployees)
        {
            emp.Designation = cleanNew;
        }

        await _context.SaveChangesAsync();
        return (true, $"Designation updated to '{cleanNew}'. Updated {affectedEmployees.Count} worker record(s).");
    }

    public async Task<(bool success, string message)> DeleteDesignationAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (false, "Designation title cannot be empty.");

        string cleanTitle = title.Trim();
        var existing = await _context.DesignationMasters.FirstOrDefaultAsync(d => d.Title.ToUpper() == cleanTitle.ToUpper());
        if (existing == null)
            return (false, "Designation not found.");

        int activeWorkersWithDesignation = await _context.Employees.CountAsync(e => e.Designation.ToUpper() == cleanTitle.ToUpper() && e.IsActive);
        if (activeWorkersWithDesignation > 0)
        {
            return (false, $"Cannot delete '{cleanTitle}' because {activeWorkersWithDesignation} active worker(s) are currently assigned to it. Reassign them first or edit the designation.");
        }

        _context.DesignationMasters.Remove(existing);
        await _context.SaveChangesAsync();
        return (true, $"Designation '{cleanTitle}' deleted successfully.");
    }

    public async Task<(int successCount, int totalCount, List<string> errors)> BulkCreateEmployeesAsync(
        List<BulkEmployeeEntryDto> entries, 
        int? defaultSubAreaId = null,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        int successCount = 0;
        var errors = new List<string>();

        var subAreas = await _context.SubAreas.Include(s => s.Area).AsNoTracking().ToListAsync();
        var areas = await _context.Areas.AsNoTracking().ToListAsync();
        var existingEmployees = await _context.Employees.Select(e => e.EmpCode.ToUpper()).ToListAsync();
        var existingCodes = new HashSet<string>(existingEmployees);

        foreach (var entry in entries)
        {
            try
            {
                string code = entry.EmpCode.Trim().ToUpper();
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                int resolvedSubAreaId = 0;
                if (!string.IsNullOrWhiteSpace(entry.CollieryNameOrId))
                {
                    string target = entry.CollieryNameOrId.Trim();
                    if (int.TryParse(target, out int id) && subAreas.Any(s => s.Id == id))
                    {
                        resolvedSubAreaId = id;
                    }
                    else
                    {
                        var match = subAreas.FirstOrDefault(s => s.SubAreaName.Trim().Equals(target, StringComparison.OrdinalIgnoreCase) ||
                                                                s.SubAreaCode.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            resolvedSubAreaId = match.Id;
                        }
                        else
                        {
                            var areaMatch = areas.FirstOrDefault(a => a.AreaName.Trim().Equals(target, StringComparison.OrdinalIgnoreCase) ||
                                                                      a.AreaCode.Trim().Equals(target, StringComparison.OrdinalIgnoreCase));
                            if (areaMatch != null)
                            {
                                var firstSub = subAreas.FirstOrDefault(s => s.AreaId == areaMatch.Id);
                                if (firstSub != null) resolvedSubAreaId = firstSub.Id;
                            }
                        }
                    }
                }

                if (resolvedSubAreaId == 0 && defaultSubAreaId.HasValue && defaultSubAreaId.Value > 0)
                {
                    resolvedSubAreaId = defaultSubAreaId.Value;
                }

                if (resolvedSubAreaId == 0)
                {
                    string locDesc = string.IsNullOrWhiteSpace(entry.CollieryNameOrId) ? "MISSING" : entry.CollieryNameOrId.Trim();
                    errors.Add($"Worker {entry.EmpCode}: Colliery location '{locDesc}' is missing or does not match any ECL Colliery Unit (case-insensitive check). Worker NOT onboarded.");
                    continue;
                }

                if (existingCodes.Contains(code))
                {
                    var emp = await _context.Employees.FirstOrDefaultAsync(e => e.EmpCode.ToUpper() == code);
                    if (emp != null)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.FullName)) emp.FullName = entry.FullName.Trim();
                        if (!string.IsNullOrWhiteSpace(entry.Designation)) emp.Designation = entry.Designation.Trim();
                        if (entry.IsHigherAuthority) emp.IsHigherAuthority = true;
                        emp.SubAreaId = resolvedSubAreaId;
                        successCount++;
                    }
                    continue;
                }

                var newEmp = new Employee
                {
                    EmpCode = code,
                    FullName = !string.IsNullOrWhiteSpace(entry.FullName) ? entry.FullName.Trim() : code,
                    Designation = !string.IsNullOrWhiteSpace(entry.Designation) ? entry.Designation.Trim() : "Colliery Worker",
                    SubAreaId = resolvedSubAreaId,
                    IsHigherAuthority = entry.IsHigherAuthority,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Employees.Add(newEmp);
                existingCodes.Add(code);
                successCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"Error on {entry.EmpCode}: {ex.Message}");
            }
        }

        if (successCount > 0)
        {
            await _context.SaveChangesAsync();

            try
            {
                var createdCodes = entries.Select(e => e.EmpCode.Trim().ToUpper()).ToList();
                var emps = await _context.Employees.Where(e => createdCodes.Contains(e.EmpCode.ToUpper())).ToListAsync();
                var empMap = emps.ToDictionary(e => e.EmpCode.ToUpper(), e => e);

                var pastPunches = await _context.AttendanceLogs
                    .Where(l => createdCodes.Contains(l.EmpCode.ToUpper()) && (l.EmployeeId == null || l.IsFlagged))
                    .ToListAsync();

                foreach (var p in pastPunches)
                {
                    if (empMap.TryGetValue(p.EmpCode.ToUpper(), out var employee))
                    {
                        p.EmployeeId = employee.Id;
                        p.Status = "Verified";
                        p.IsFlagged = false;
                    }
                }

                var pastSummaries = await _context.AttendanceDailySummaries
                    .Where(s => createdCodes.Contains(s.EmpCode.ToUpper()) && (s.EmployeeId == null || s.IsFlagged))
                    .ToListAsync();

                foreach (var s in pastSummaries)
                {
                    if (empMap.TryGetValue(s.EmpCode.ToUpper(), out var employee))
                    {
                        s.EmployeeId = employee.Id;
                        s.EmployeeName = employee.FullName;
                        s.Designation = employee.Designation;
                        s.IsVerified = true;
                        s.IsFlagged = false;
                        s.IsHigherAuthority = employee.IsHigherAuthority;
                    }
                }

                int hardwareSyncedCount = 0;
                int hardwareFailedCount = 0;

                // Dispatch SOAP update with location code for each onboarded/updated worker
                foreach (var emp in emps)
                {
                    try
                    {
                        string locationCodes = await BuildLocationCodesAsync(emp.SubAreaId, emp.AdditionalSubAreaIds);
                        if (!string.IsNullOrWhiteSpace(locationCodes))
                        {
                            var (soapSuccess, _) = await _ebiosSoapService.UpdateEmployeeAsync(
                                emp.EmpCode,
                                emp.FullName,
                                locationCodes,
                                "Employee",
                                "None"
                            );

                            if (soapSuccess)
                            {
                                emp.IsSyncedToEbios = true;
                                emp.LastSyncedAt = DateTime.UtcNow;
                                hardwareSyncedCount++;
                            }
                            else
                            {
                                hardwareFailedCount++;
                            }
                        }
                    }
                    catch (Exception soapEx)
                    {
                        hardwareFailedCount++;
                        _logger.LogWarning("SOAP sync warning in bulk onboarding for {Code}: {Msg}", emp.EmpCode, soapEx.Message);
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Retroactive linking warning during bulk employee import: {Msg}", ex.Message);
            }
        }

        if (successCount > 0)
        {
            var bulkSummary = new
            {
                SuccessCount = successCount,
                TotalCount = entries.Count,
                ErrorCount = errors.Count,
                SampleErrors = errors.Take(5).ToList()
            };

            await _auditService.LogSecurityActionAsync(
                "BULK_WORKERS_IMPORTED",
                $"BATCH:{successCount}_WORKERS",
                $"Bulk CSV Import: Successfully saved {successCount} of {entries.Count} workers to local database by {performedByUsername ?? "Admin"}.",
                performedByUserId,
                performedByUsername ?? "admin",
                performedByOfficerName ?? performedByUsername ?? "System Admin",
                performedByRole ?? "Officer",
                null,
                JsonSerializer.Serialize(bulkSummary));
        }

        return (successCount, entries.Count, errors);
    }
}
