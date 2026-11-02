using Ebios.Application.DTOs;

namespace Ebios.Application.Interfaces;

public interface IAreaService
{
    Task<List<AreaDto>> GetAllAreasAsync();
    Task<AreaDto?> GetAreaByIdAsync(int id);
    Task<AreaDto> CreateAreaAsync(CreateAreaDto dto);
    Task<bool> UpdateAreaAsync(int id, CreateAreaDto dto);

    Task<List<SubAreaDto>> GetSubAreasAsync(int? areaId = null);
    Task<SubAreaDto?> GetSubAreaByIdAsync(int id);
    Task<SubAreaDto> CreateSubAreaAsync(CreateSubAreaDto dto);
    Task<bool> UpdateSubAreaAsync(int id, CreateSubAreaDto dto);
}
