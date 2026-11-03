using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ebios.Infrastructure.Services;

public class ShiftService : IShiftService
{
    private readonly AppDbContext _context;

    public ShiftService(AppDbContext context)
    {
        _context = context;
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
}
