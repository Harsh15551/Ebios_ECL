using System.Text.Json;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ebios.Infrastructure.Services;

public class ShiftService : IShiftService
{
    private readonly AppDbContext _context;
    private readonly IAuditService _auditService;

    public ShiftService(AppDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public async Task<List<ShiftMasterDto>> GetAllShiftsAsync()
    {
        return await _context.ShiftMasters
            .AsNoTracking()
            .OrderBy(s => s.StartTime)
            .Select(s => new ShiftMasterDto(
                s.Id,
                s.ShiftCode,
                s.ShiftName,
                s.StartTime,
                s.EndTime,
                s.InWindowStart,
                s.InWindowEnd,
                s.OutWindowStart,
                s.OutWindowEnd,
                s.GraceMinutes,
                s.DurationHours,
                s.IsNightShift,
                s.IsActive
            ))
            .ToListAsync();
    }

    public async Task<ShiftMasterDto?> GetShiftByCodeAsync(string shiftCode)
    {
        if (string.IsNullOrWhiteSpace(shiftCode)) return null;

        var s = await _context.ShiftMasters
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShiftCode.ToUpper() == shiftCode.Trim().ToUpper());

        if (s == null) return null;

        return new ShiftMasterDto(
            s.Id,
            s.ShiftCode,
            s.ShiftName,
            s.StartTime,
            s.EndTime,
            s.InWindowStart,
            s.InWindowEnd,
            s.OutWindowStart,
            s.OutWindowEnd,
            s.GraceMinutes,
            s.DurationHours,
            s.IsNightShift,
            s.IsActive
        );
    }

    public async Task<ShiftMasterDto?> GetShiftByIdAsync(int id)
    {
        var s = await _context.ShiftMasters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return null;

        return new ShiftMasterDto(
            s.Id,
            s.ShiftCode,
            s.ShiftName,
            s.StartTime,
            s.EndTime,
            s.InWindowStart,
            s.InWindowEnd,
            s.OutWindowStart,
            s.OutWindowEnd,
            s.GraceMinutes,
            s.DurationHours,
            s.IsNightShift,
            s.IsActive
        );
    }

    public async Task<bool> CreateShiftAsync(
        CreateShiftDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        if (string.IsNullOrWhiteSpace(dto.ShiftCode) || string.IsNullOrWhiteSpace(dto.ShiftName))
            return false;

        string cleanCode = dto.ShiftCode.Trim();
        bool exists = await _context.ShiftMasters.AnyAsync(s => s.ShiftCode.ToUpper() == cleanCode.ToUpper());
        if (exists) return false;

        var shift = new Ebios.Domain.Entities.ShiftMaster
        {
            ShiftCode = cleanCode,
            ShiftName = dto.ShiftName.Trim(),
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            InWindowStart = dto.InWindowStart,
            InWindowEnd = dto.InWindowEnd,
            OutWindowStart = dto.OutWindowStart,
            OutWindowEnd = dto.OutWindowEnd,
            GraceMinutes = dto.GraceMinutes > 0 ? dto.GraceMinutes : 30,
            DurationHours = dto.DurationHours > 0 ? dto.DurationHours : 8.0,
            IsNightShift = dto.IsNightShift,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.ShiftMasters.Add(shift);
        await _context.SaveChangesAsync();

        var newState = new
        {
            shift.Id,
            shift.ShiftCode,
            shift.ShiftName,
            shift.StartTime,
            shift.EndTime,
            shift.GraceMinutes,
            shift.DurationHours,
            shift.IsNightShift,
            shift.IsActive
        };

        await _auditService.LogSecurityActionAsync(
            "SHIFT_CREATED",
            $"SHIFT:{shift.ShiftCode}",
            $"Shift timing '{shift.ShiftName}' ({shift.ShiftCode}) [{shift.StartTime}-{shift.EndTime}] created by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            null,
            JsonSerializer.Serialize(newState));

        return true;
    }

    public async Task<bool> UpdateShiftAsync(
        int id, 
        UpdateShiftDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var shift = await _context.ShiftMasters.FindAsync(id);
        if (shift == null) return false;

        string cleanCode = dto.ShiftCode.Trim();
        bool codeConflict = await _context.ShiftMasters.AnyAsync(s => s.Id != id && s.ShiftCode.ToUpper() == cleanCode.ToUpper());
        if (codeConflict) return false;

        var previousState = new
        {
            shift.Id,
            shift.ShiftCode,
            shift.ShiftName,
            shift.StartTime,
            shift.EndTime,
            shift.GraceMinutes,
            shift.DurationHours,
            shift.IsNightShift,
            shift.IsActive
        };

        shift.ShiftCode = cleanCode;
        shift.ShiftName = dto.ShiftName.Trim();
        shift.StartTime = dto.StartTime;
        shift.EndTime = dto.EndTime;
        shift.InWindowStart = dto.InWindowStart;
        shift.InWindowEnd = dto.InWindowEnd;
        shift.OutWindowStart = dto.OutWindowStart;
        shift.OutWindowEnd = dto.OutWindowEnd;
        shift.GraceMinutes = dto.GraceMinutes > 0 ? dto.GraceMinutes : 30;
        shift.DurationHours = dto.DurationHours > 0 ? dto.DurationHours : 8.0;
        shift.IsNightShift = dto.IsNightShift;
        shift.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        var newState = new
        {
            shift.Id,
            shift.ShiftCode,
            shift.ShiftName,
            shift.StartTime,
            shift.EndTime,
            shift.GraceMinutes,
            shift.DurationHours,
            shift.IsNightShift,
            shift.IsActive
        };

        await _auditService.LogSecurityActionAsync(
            "SHIFT_UPDATED",
            $"SHIFT:{shift.ShiftCode}",
            $"Shift timing '{shift.ShiftName}' ({shift.ShiftCode}) [{shift.StartTime}-{shift.EndTime}] modified by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            JsonSerializer.Serialize(previousState),
            JsonSerializer.Serialize(newState));

        return true;
    }

    public async Task<bool> DeleteShiftAsync(
        int id,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var shift = await _context.ShiftMasters.FindAsync(id);
        if (shift == null) return false;

        // Check if roster or summary uses this shift
        bool inUse = await _context.EmployeeShiftRosters.AnyAsync(r => r.ShiftId == id) ||
                     await _context.AttendanceDailySummaries.AnyAsync(s => s.ShiftId == id);

        if (inUse)
        {
            // Soft delete
            shift.IsActive = false;
        }
        else
        {
            _context.ShiftMasters.Remove(shift);
        }

        await _context.SaveChangesAsync();

        await _auditService.LogSecurityActionAsync(
            "SHIFT_DELETED",
            $"SHIFT:{shift.ShiftCode}",
            $"Shift timing '{shift.ShiftName}' ({shift.ShiftCode}) was {(inUse ? "deactivated" : "deleted")} by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            null,
            null);

        return true;
    }
}

