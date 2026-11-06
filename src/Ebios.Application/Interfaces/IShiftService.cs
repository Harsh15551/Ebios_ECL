using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IShiftService
{
    Task<List<ShiftMasterDto>> GetAllShiftsAsync();
    Task<ShiftMasterDto?> GetShiftByCodeAsync(string shiftCode);
    Task<ShiftMasterDto?> GetShiftByIdAsync(int id);
    Task<bool> CreateShiftAsync(
        CreateShiftDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<bool> UpdateShiftAsync(
        int id, 
        UpdateShiftDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<bool> DeleteShiftAsync(
        int id,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
}

