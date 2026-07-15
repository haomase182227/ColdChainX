using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;
using ColdChainX.Application.DTOs.WarehouseFlow;

namespace ColdChainX.Application.Interfaces
{
    public interface IWarehouseService
    {
        Task<ApiResponse<WarehouseResponse>> CreateAsync(CreateWarehouseRequest request, Guid currentUserId);
        Task<ApiResponse<WarehouseResponse>> UpdateAsync(Guid warehouseId, UpdateWarehouseRequest request, Guid currentUserId);
        Task<ApiResponse<bool>> DeleteAsync(Guid warehouseId, Guid currentUserId);
        Task<ApiResponse<WarehouseResponse>> GetByIdAsync(Guid warehouseId);
        Task<ApiResponse<PagedResult<WarehouseResponse>>> GetListAsync(int pageNumber, int pageSize, string? search = null);
        Task<ApiResponse<PagedResult<LpnResponse>>> GetLpnsInWarehouseAsync(Guid warehouseId, int page, int pageSize);
    }
}
