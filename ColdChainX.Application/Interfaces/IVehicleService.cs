using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IVehicleService
    {
        Task<ApiResponse<List<VehicleDto>>> GetAllAsync();
        Task<ApiResponse<VehicleDto>> GetByIdAsync(Guid id);
        Task<ApiResponse<VehicleDto>> CreateAsync(VehicleCreateRequest request);
        Task<ApiResponse<VehicleDto>> UpdateAsync(Guid id, VehicleUpdateRequest request);
        Task<ApiResponse<bool>> DeleteAsync(Guid id);
    }
}