using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.WarehouseLocation;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IWarehouseLocationService
    {
        Task<ApiResponse<WarehouseLocationResponse>> CreateAsync(Guid zoneId, CreateWarehouseLocationRequest request, Guid currentUserId);
        Task<ApiResponse<WarehouseLocationResponse>> UpdateAsync(Guid locationId, UpdateWarehouseLocationRequest request, Guid currentUserId);
        Task<ApiResponse<bool>> DeleteAsync(Guid locationId, Guid currentUserId);
        Task<ApiResponse<WarehouseLocationResponse>> GetByIdAsync(Guid locationId);
        Task<ApiResponse<PagedResult<WarehouseLocationResponse>>> GetListAsync(Guid zoneId, int pageNumber, int pageSize, string? search = null);
    }
}
