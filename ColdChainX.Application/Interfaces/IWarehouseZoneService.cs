using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.WarehouseZone;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IWarehouseZoneService
    {
        Task<ApiResponse<WarehouseZoneResponse>> CreateAsync(Guid warehouseId, CreateWarehouseZoneRequest request, Guid currentUserId);
        Task<ApiResponse<WarehouseZoneResponse>> UpdateAsync(Guid zoneId, UpdateWarehouseZoneRequest request, Guid currentUserId);
        Task<ApiResponse<bool>> DeleteAsync(Guid zoneId, Guid currentUserId);
        Task<ApiResponse<WarehouseZoneResponse>> GetByIdAsync(Guid zoneId);
        Task<ApiResponse<PagedResult<WarehouseZoneResponse>>> GetListAsync(Guid warehouseId, int pageNumber, int pageSize, string? search = null);
    }
}
