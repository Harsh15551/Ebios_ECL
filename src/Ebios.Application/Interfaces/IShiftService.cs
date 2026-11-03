using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IShiftService
{
    Task<List<ShiftMasterDto>> GetAllShiftsAsync();
    Task<ShiftMasterDto?> GetShiftByCodeAsync(string shiftCode);
}
