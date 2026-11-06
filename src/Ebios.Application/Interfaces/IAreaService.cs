using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IAreaService
{
    Task<List<AreaDto>> GetAllAreasAsync();
    Task<AreaDto?> GetAreaByIdAsync(int id);
    Task<AreaDto> CreateAreaAsync(
        CreateAreaDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<bool> UpdateAreaAsync(
        int id, 
        CreateAreaDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);

    Task<List<SubAreaDto>> GetSubAreasAsync(int? areaId = null);
    Task<SubAreaDto?> GetSubAreaByIdAsync(int id);
    Task<SubAreaDto> CreateSubAreaAsync(
        CreateSubAreaDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
    Task<bool> UpdateSubAreaAsync(
        int id, 
        CreateSubAreaDto dto,
        int? performedByUserId = null,
        string? performedByUsername = null,
        string? performedByOfficerName = null,
        string? performedByRole = null);
}
