using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IShiftService
{
    Task<List<ShiftMasterDto>> GetAllShiftsAsync();
    Task<ShiftMasterDto?> GetShiftByCodeAsync(string shiftCode);
    Task<ShiftMasterDto?> GetShiftByIdAsync(int id);
    Task<bool> CreateShiftAsync(CreateShiftDto dto);
    Task<bool> UpdateShiftAsync(int id, UpdateShiftDto dto);
    Task<bool> DeleteShiftAsync(int id);
}

