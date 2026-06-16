using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Shared.Responses;
using ColdChainX.Application.DTOs.Common;

namespace ColdChainX.Application.Interfaces
{
    public interface IInventoryHoldService
    {
        Task<ApiResponse<HoldResponseDto>> CreateHoldAsync(CreateInventoryHoldDto dto, Guid userId);
        Task<ApiResponse<bool>> ReleaseHoldAsync(Guid holdId, ReleaseInventoryHoldDto dto, Guid userId);
        Task<ApiResponse<PagedResult<HoldResponseDto>>> GetPagedHoldsAsync(int pageNumber, int pageSize, string? status, string? reasonCode, string? itemCode);
        Task<ApiResponse<bool>> AdjustOutHoldAsync(Guid holdId, string reasonNotes, Guid userId);
        Task<ApiResponse<HoldResponseDto>> GetHoldByIdAsync(Guid holdId);
    }
}
