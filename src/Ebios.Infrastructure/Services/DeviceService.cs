using System.Text.Json;
using Ebios.Application.DTOs;
using Ebios.Application.Interfaces;
using Ebios.Domain.Entities;
using Ebios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class DeviceService : IDeviceService
{
    private readonly AppDbContext _context;
    private readonly IEbiosSoapService _ebiosSoapService;
    private readonly IAuditService _auditService;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(
        AppDbContext context, 
        IEbiosSoapService ebiosSoapService, 
        IAuditService auditService,
        ILogger<DeviceService> logger)
    {
        _context = context;
        _ebiosSoapService = ebiosSoapService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<List<DeviceDto>> GetDevicesAsync(int? areaId = null, int? subAreaId = null)
    {
        var query = _context.Devices
            .AsNoTracking()
            .Include(d => d.SubArea)
            .ThenInclude(s => s!.Area)
            .AsQueryable();

        if (subAreaId.HasValue)
        {
            query = query.Where(d => d.SubAreaId == subAreaId.Value);
        }
        else if (areaId.HasValue)
        {
            query = query.Where(d => d.SubArea != null && d.SubArea.AreaId == areaId.Value);
        }

        return await query
            .OrderBy(d => d.DeviceName)
            .Select(d => new DeviceDto(
                d.Id,
                d.SerialNumber,
                d.DeviceName,
                d.Direction,
                d.DeviceType,
                d.ActivationCode ?? "0",
                d.SubAreaId,
                d.SubArea != null ? d.SubArea.SubAreaName : string.Empty,
                d.SubArea != null && d.SubArea.Area != null ? d.SubArea.Area.AreaName : string.Empty,
                d.IsActive,
                d.IsSyncedToEbios,
                d.LastSyncedAt,
                d.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<DeviceDto?> GetDeviceByIdAsync(int id)
    {
        var d = await _context.Devices
            .AsNoTracking()
            .Include(x => x.SubArea)
            .ThenInclude(s => s!.Area)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (d == null) return null;

        return new DeviceDto(
            d.Id,
            d.SerialNumber,
            d.DeviceName,
            d.Direction,
            d.DeviceType,
            d.ActivationCode ?? "0",
            d.SubAreaId,
            d.SubArea != null ? d.SubArea.SubAreaName : string.Empty,
            d.SubArea != null && d.SubArea.Area != null ? d.SubArea.Area.AreaName : string.Empty,
            d.IsActive,
            d.IsSyncedToEbios,
            d.LastSyncedAt,
            d.CreatedAt
        );
    }

    public async Task<DeviceDto> CreateDeviceAsync(
        CreateDeviceDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        string normalizedSerial = dto.SerialNumber.Trim().ToUpperInvariant();
        if (await _context.Devices.AnyAsync(d => d.SerialNumber == normalizedSerial))
        {
            throw new InvalidOperationException($"Biometric device with serial number '{normalizedSerial}' already exists.");
        }

        var subArea = await _context.SubAreas.Include(s => s.Area).FirstOrDefaultAsync(s => s.Id == dto.SubAreaId);
        if (subArea == null)
        {
            throw new InvalidOperationException($"Colliery Sub-Area with ID {dto.SubAreaId} was not found.");
        }

        string normalizedDirection = (dto.Direction ?? "INOUT").Trim().ToUpperInvariant() switch
        {
            "IN" => "IN",
            "OUT" => "OUT",
            _ => "INOUT"
        };

        string normalizedDeviceType = (dto.DeviceType ?? "AI").Trim().ToUpperInvariant() switch
        {
            "NON AI" or "NONAI" or "NON-AI" => "Non AI",
            _ => "AI"
        };

        var device = new Device
        {
            SerialNumber = normalizedSerial,
            DeviceName = dto.DeviceName.Trim(),
            Direction = normalizedDirection,
            DeviceType = normalizedDeviceType,
            TimeZone = "330",
            ActivationCode = string.IsNullOrWhiteSpace(dto.ActivationCode) ? "0" : dto.ActivationCode.Trim(),
            SubAreaId = dto.SubAreaId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Devices.Add(device);
        await _context.SaveChangesAsync();

        var newState = new
        {
            device.Id,
            device.SerialNumber,
            device.DeviceName,
            device.Direction,
            device.DeviceType,
            SubAreaName = subArea.SubAreaName,
            AreaName = subArea.Area?.AreaName,
            device.IsActive
        };

        await _auditService.LogSecurityActionAsync(
            "DEVICE_REGISTERED",
            $"DEV:{device.SerialNumber}",
            $"Turnstile terminal '{device.DeviceName}' ({device.SerialNumber}) registered at [{subArea.SubAreaName}] by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            null,
            JsonSerializer.Serialize(newState));

        // Background auto-sync to eBios SOAP Server
        try
        {
            // Ensure location is synced in eBioServer first so foreign key exists
            await _ebiosSoapService.UpdateLocationAsync(subArea.SubAreaCode, $"{subArea.Area?.AreaName ?? "Area"} - {subArea.SubAreaName}");

            var (success, msg) = await _ebiosSoapService.UpdateDeviceAsync(
                device.SerialNumber,
                device.DeviceName,
                device.Direction,
                device.DeviceType,
                "330",
                device.ActivationCode,
                subArea.SubAreaCode
            );

            if (success)
            {
                device.IsSyncedToEbios = true;
                device.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully synced Device {Serial} to eBios.", device.SerialNumber);
            }
            else
            {
                _logger.LogWarning("eBios UpdateDevice returned non-success for {Serial}: {Msg}", device.SerialNumber, msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-sync Device {Serial} to eBios server.", device.SerialNumber);
        }

        return new DeviceDto(
            device.Id,
            device.SerialNumber,
            device.DeviceName,
            device.Direction,
            device.DeviceType,
            device.ActivationCode,
            device.SubAreaId,
            subArea.SubAreaName,
            subArea.Area != null ? subArea.Area.AreaName : string.Empty,
            device.IsActive,
            device.IsSyncedToEbios,
            device.LastSyncedAt,
            device.CreatedAt
        );
    }

    public async Task<DeviceDto> UpdateDeviceAsync(
        int id,
        UpdateDeviceDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var device = await _context.Devices.Include(d => d.SubArea).ThenInclude(s => s!.Area).FirstOrDefaultAsync(d => d.Id == id);
        if (device == null)
        {
            throw new InvalidOperationException($"Biometric device with ID {id} was not found.");
        }

        var subArea = await _context.SubAreas.Include(s => s.Area).FirstOrDefaultAsync(s => s.Id == dto.SubAreaId);
        if (subArea == null)
        {
            throw new InvalidOperationException($"Colliery Sub-Area with ID {dto.SubAreaId} was not found.");
        }

        string normalizedDirection = (dto.Direction ?? "INOUT").Trim().ToUpperInvariant() switch
        {
            "IN" => "IN",
            "OUT" => "OUT",
            _ => "INOUT"
        };

        string normalizedDeviceType = (dto.DeviceType ?? "AI").Trim().ToUpperInvariant() switch
        {
            "NON AI" or "NONAI" or "NON-AI" => "Non AI",
            _ => "AI"
        };

        var oldState = new
        {
            device.Id,
            device.SerialNumber,
            device.DeviceName,
            device.Direction,
            device.DeviceType,
            device.ActivationCode,
            SubAreaName = device.SubArea?.SubAreaName,
            AreaName = device.SubArea?.Area?.AreaName,
            device.IsActive
        };

        device.DeviceName = dto.DeviceName.Trim();
        device.Direction = normalizedDirection;
        device.DeviceType = normalizedDeviceType;
        device.ActivationCode = string.IsNullOrWhiteSpace(dto.ActivationCode) ? "0" : dto.ActivationCode.Trim();
        device.SubAreaId = dto.SubAreaId;

        await _context.SaveChangesAsync();

        var newState = new
        {
            device.Id,
            device.SerialNumber,
            device.DeviceName,
            device.Direction,
            device.DeviceType,
            device.ActivationCode,
            SubAreaName = subArea.SubAreaName,
            AreaName = subArea.Area?.AreaName,
            device.IsActive
        };

        await _auditService.LogSecurityActionAsync(
            "DEVICE_UPDATED",
            $"DEV:{device.SerialNumber}",
            $"Turnstile terminal '{device.DeviceName}' ({device.SerialNumber}) updated at [{subArea.SubAreaName}] by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            JsonSerializer.Serialize(oldState),
            JsonSerializer.Serialize(newState));

        // Push update to eBioServer SOAP API (UpdateDevice method)
        try
        {
            await _ebiosSoapService.UpdateLocationAsync(subArea.SubAreaCode, $"{subArea.Area?.AreaName ?? "Area"} - {subArea.SubAreaName}");

            var (success, msg) = await _ebiosSoapService.UpdateDeviceAsync(
                device.SerialNumber,
                device.DeviceName,
                device.Direction,
                device.DeviceType,
                "330",
                device.ActivationCode,
                subArea.SubAreaCode
            );

            if (success)
            {
                device.IsSyncedToEbios = true;
                device.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully updated Device {Serial} on eBios server.", device.SerialNumber);
            }
            else
            {
                _logger.LogWarning("eBios UpdateDevice returned non-success for {Serial}: {Msg}", device.SerialNumber, msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync Device {Serial} update to eBios server.", device.SerialNumber);
        }

        return new DeviceDto(
            device.Id,
            device.SerialNumber,
            device.DeviceName,
            device.Direction,
            device.DeviceType,
            device.ActivationCode ?? "0",
            device.SubAreaId,
            subArea.SubAreaName,
            subArea.Area != null ? subArea.Area.AreaName : string.Empty,
            device.IsActive,
            device.IsSyncedToEbios,
            device.LastSyncedAt,
            device.CreatedAt
        );
    }

    public async Task<bool> ToggleDeviceStatusAsync(
        int id,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var device = await _context.Devices.Include(d => d.SubArea).FirstOrDefaultAsync(d => d.Id == id);
        if (device == null) return false;

        bool oldStatus = device.IsActive;
        device.IsActive = !device.IsActive;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Toggled active status for Device {Serial} to {Status}", device.SerialNumber, device.IsActive);

        var previousState = new { device.Id, device.SerialNumber, device.DeviceName, IsActive = oldStatus };
        var newState = new { device.Id, device.SerialNumber, device.DeviceName, IsActive = device.IsActive };

        await _auditService.LogSecurityActionAsync(
            "DEVICE_STATUS_CHANGED",
            $"DEV:{device.SerialNumber}",
            $"Turnstile terminal '{device.DeviceName}' ({device.SerialNumber}) was {(device.IsActive ? "Enabled" : "Disabled")} by {performedByUsername ?? "Admin"}.",
            performedByUserId,
            performedByUsername ?? "admin",
            performedByOfficerName ?? performedByUsername ?? "System Admin",
            performedByRole ?? "Officer",
            JsonSerializer.Serialize(previousState),
            JsonSerializer.Serialize(newState));

        return true;
    }

    public async Task<(bool success, string message)> SyncDeviceAsync(int id)
    {
        var device = await _context.Devices.Include(d => d.SubArea).FirstOrDefaultAsync(d => d.Id == id);
        if (device == null || device.SubArea == null) return (false, "Device or associated Colliery Unit not found.");

        try
        {
            string normDirection = (device.Direction ?? "INOUT").ToUpperInvariant() switch
            {
                "IN" => "IN",
                "OUT" => "OUT",
                _ => "INOUT"
            };

            string normDeviceType = (device.DeviceType ?? "AI").Trim().ToUpperInvariant() switch
            {
                "NON AI" or "NONAI" or "NON-AI" => "Non AI",
                _ => "AI"
            };

            await _ebiosSoapService.UpdateLocationAsync(device.SubArea.SubAreaCode, $"{device.SubArea.SubAreaName}");

            var (success, msg) = await _ebiosSoapService.UpdateDeviceAsync(
                device.SerialNumber,
                device.DeviceName,
                normDirection,
                normDeviceType,
                "330",
                device.ActivationCode ?? "0",
                device.SubArea.SubAreaCode
            );

            if (success)
            {
                device.IsSyncedToEbios = true;
                device.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return (true, $"Device '{device.DeviceName}' ({device.SerialNumber}) synchronized successfully with eBios.");
            }

            _logger.LogWarning("Re-sync for device {Serial} failed: {Msg}", device.SerialNumber, msg);
            return (false, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while re-syncing device {Serial}", device.SerialNumber);
            return (false, ex.Message);
        }
    }

    public async Task<(bool success, string message)> ActivateDeviceAsync(
        int id, 
        string activationCode,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null)
    {
        var device = await _context.Devices.Include(d => d.SubArea).FirstOrDefaultAsync(d => d.Id == id);
        if (device == null || device.SubArea == null) return (false, "Device not found.");

        string normCode = string.IsNullOrWhiteSpace(activationCode) ? "0" : activationCode.Trim();

        try
        {
            string normDirection = (device.Direction ?? "INOUT").ToUpperInvariant() switch
            {
                "IN" => "IN",
                "OUT" => "OUT",
                _ => "INOUT"
            };

            string normDeviceType = (device.DeviceType ?? "AI").Trim().ToUpperInvariant() switch
            {
                "NON AI" or "NONAI" or "NON-AI" => "Non AI",
                _ => "AI"
            };

            await _ebiosSoapService.UpdateLocationAsync(device.SubArea.SubAreaCode, $"{device.SubArea.SubAreaName}");

            var (success, msg) = await _ebiosSoapService.UpdateDeviceAsync(
                device.SerialNumber,
                device.DeviceName,
                normDirection,
                normDeviceType,
                "330",
                normCode,
                device.SubArea.SubAreaCode
            );

            if (success)
            {
                string oldCode = device.ActivationCode ?? "0";
                device.ActivationCode = normCode;
                device.IsSyncedToEbios = true;
                device.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Device {Serial} activated successfully with code {Code}.", device.SerialNumber, device.ActivationCode);

                var previousState = new { device.Id, device.SerialNumber, device.DeviceName, ActivationCode = oldCode };
                var newState = new { device.Id, device.SerialNumber, device.DeviceName, ActivationCode = device.ActivationCode, device.IsSyncedToEbios };

                await _auditService.LogSecurityActionAsync(
                    "DEVICE_ACTIVATED",
                    $"DEV:{device.SerialNumber}",
                    $"Turnstile terminal '{device.DeviceName}' ({device.SerialNumber}) paired and activated with code [{normCode}] by {performedByUsername ?? "Admin"}.",
                    performedByUserId,
                    performedByUsername ?? "admin",
                    performedByOfficerName ?? performedByUsername ?? "System Admin",
                    performedByRole ?? "Officer",
                    JsonSerializer.Serialize(previousState),
                    JsonSerializer.Serialize(newState));

                return (true, $"Terminal '{device.DeviceName}' ({device.SerialNumber}) activated and synchronized with biometric server.");
            }
            else
            {
                _logger.LogWarning("Activation for device {Serial} returned: {Msg}", device.SerialNumber, msg);
                return (false, $"Biometric server activation failed: {msg}. Terminal activation code was not updated.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating device {Serial}", device.SerialNumber);
            return (false, "Biometric server communication error. Terminal activation code was not updated.");
        }
    }
}
