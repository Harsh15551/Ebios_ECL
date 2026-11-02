using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class AreaService : IAreaService
{
    private readonly AppDbContext _context;
    private readonly IEbiosSoapService _ebiosSoapService;
    private readonly ILogger<AreaService> _logger;

    public AreaService(AppDbContext context, IEbiosSoapService ebiosSoapService, ILogger<AreaService> logger)
    {
        _context = context;
        _ebiosSoapService = ebiosSoapService;
        _logger = logger;
    }

    public async Task<List<AreaDto>> GetAllAreasAsync()
    {
        return await _context.Areas
            .AsNoTracking()
            .OrderBy(a => a.AreaName)
            .Select(a => new AreaDto(
                a.Id,
                a.AreaCode,
                a.AreaName,
                a.Headquarters,
                a.IsActive,
                a.SubAreas.Count,
                a.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<AreaDto?> GetAreaByIdAsync(int id)
    {
        var a = await _context.Areas
            .AsNoTracking()
            .Include(x => x.SubAreas)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (a == null) return null;

        return new AreaDto(a.Id, a.AreaCode, a.AreaName, a.Headquarters, a.IsActive, a.SubAreas.Count, a.CreatedAt);
    }

    public async Task<AreaDto> CreateAreaAsync(CreateAreaDto dto)
    {
        string normalizedCode = dto.AreaCode.Trim().ToUpperInvariant();
        if (await _context.Areas.AnyAsync(a => a.AreaCode == normalizedCode))
        {
            throw new InvalidOperationException($"Area with code '{normalizedCode}' already exists.");
        }

        var area = new Area
        {
            AreaCode = normalizedCode,
            AreaName = dto.AreaName.Trim(),
            Headquarters = string.IsNullOrWhiteSpace(dto.Headquarters) ? "ECL HQ" : dto.Headquarters.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Areas.Add(area);
        await _context.SaveChangesAsync();

        return new AreaDto(area.Id, area.AreaCode, area.AreaName, area.Headquarters, area.IsActive, 0, area.CreatedAt);
    }

    public async Task<bool> UpdateAreaAsync(int id, CreateAreaDto dto)
    {
        var area = await _context.Areas.FindAsync(id);
        if (area == null) return false;

        string normalizedCode = dto.AreaCode.Trim().ToUpperInvariant();
        if (area.AreaCode != normalizedCode && await _context.Areas.AnyAsync(a => a.AreaCode == normalizedCode))
        {
            throw new InvalidOperationException($"Area with code '{normalizedCode}' already exists.");
        }

        area.AreaCode = normalizedCode;
        area.AreaName = dto.AreaName.Trim();
        area.Headquarters = string.IsNullOrWhiteSpace(dto.Headquarters) ? "ECL HQ" : dto.Headquarters.Trim();

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<SubAreaDto>> GetSubAreasAsync(int? areaId = null)
    {
        var query = _context.SubAreas
            .AsNoTracking()
            .Include(s => s.Area)
            .AsQueryable();

        if (areaId.HasValue)
        {
            query = query.Where(s => s.AreaId == areaId.Value);
        }

        return await query
            .OrderBy(s => s.SubAreaName)
            .Select(s => new SubAreaDto(
                s.Id,
                s.SubAreaCode,
                s.SubAreaName,
                s.AreaId,
                s.Area != null ? s.Area.AreaName : string.Empty,
                s.IsSyncedToEbios,
                s.LastSyncedAt,
                s.IsActive,
                s.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<SubAreaDto?> GetSubAreaByIdAsync(int id)
    {
        var s = await _context.SubAreas
            .AsNoTracking()
            .Include(x => x.Area)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (s == null) return null;

        return new SubAreaDto(
            s.Id,
            s.SubAreaCode,
            s.SubAreaName,
            s.AreaId,
            s.Area != null ? s.Area.AreaName : string.Empty,
            s.IsSyncedToEbios,
            s.LastSyncedAt,
            s.IsActive,
            s.CreatedAt);
    }

    public async Task<SubAreaDto> CreateSubAreaAsync(CreateSubAreaDto dto)
    {
        string normalizedCode = dto.SubAreaCode.Trim().ToUpperInvariant();
        if (await _context.SubAreas.AnyAsync(s => s.SubAreaCode == normalizedCode))
        {
            throw new InvalidOperationException($"Sub-Area with code '{normalizedCode}' already exists.");
        }

        var area = await _context.Areas.FindAsync(dto.AreaId);
        if (area == null)
        {
            throw new InvalidOperationException($"Parent Area with ID {dto.AreaId} does not exist.");
        }

        var subArea = new SubArea
        {
            SubAreaCode = normalizedCode,
            SubAreaName = dto.SubAreaName.Trim(),
            AreaId = dto.AreaId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.SubAreas.Add(subArea);
        await _context.SaveChangesAsync();

        // Sync SubArea as Location to eBios SOAP server
        try
        {
            string description = $"{area.AreaName} - {subArea.SubAreaName}";
            var (success, msg) = await _ebiosSoapService.UpdateLocationAsync(subArea.SubAreaCode, description);
            if (success)
            {
                subArea.IsSyncedToEbios = true;
                subArea.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully synced SubArea {Code} to eBios as Location.", subArea.SubAreaCode);
            }
            else
            {
                _logger.LogWarning("eBios UpdateLocation returned non-success for {Code}: {Msg}", subArea.SubAreaCode, msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync SubArea {Code} to eBios server.", subArea.SubAreaCode);
        }

        return new SubAreaDto(
            subArea.Id,
            subArea.SubAreaCode,
            subArea.SubAreaName,
            subArea.AreaId,
            area.AreaName,
            subArea.IsSyncedToEbios,
            subArea.LastSyncedAt,
            subArea.IsActive,
            subArea.CreatedAt);
    }

    public async Task<bool> UpdateSubAreaAsync(int id, CreateSubAreaDto dto)
    {
        var subArea = await _context.SubAreas.Include(s => s.Area).FirstOrDefaultAsync(s => s.Id == id);
        if (subArea == null) return false;

        string normalizedCode = dto.SubAreaCode.Trim().ToUpperInvariant();
        if (subArea.SubAreaCode != normalizedCode && await _context.SubAreas.AnyAsync(s => s.SubAreaCode == normalizedCode))
        {
            throw new InvalidOperationException($"Sub-Area with code '{normalizedCode}' already exists.");
        }

        subArea.SubAreaCode = normalizedCode;
        subArea.SubAreaName = dto.SubAreaName.Trim();
        subArea.AreaId = dto.AreaId;

        await _context.SaveChangesAsync();

        // Update in eBios as well
        try
        {
            var area = await _context.Areas.FindAsync(dto.AreaId);
            string description = $"{(area != null ? area.AreaName : "ECL")} - {subArea.SubAreaName}";
            var (success, _) = await _ebiosSoapService.UpdateLocationAsync(subArea.SubAreaCode, description);
            if (success)
            {
                subArea.IsSyncedToEbios = true;
                subArea.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update location in eBios for {Code}", subArea.SubAreaCode);
        }

        return true;
    }
}
