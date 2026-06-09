using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IDriverService
    {
        Task<ApiResponse<List<DriverDto>>> GetAllAsync();
        Task<ApiResponse<DriverDto>> GetByIdAsync(Guid id);
        Task<ApiResponse<DriverDto>> CreateAsync(DriverCreateRequest request);
        Task<ApiResponse<DriverDto>> UpdateAsync(Guid id, DriverUpdateRequest request);
        Task<ApiResponse<bool>> DeleteAsync(Guid id);
    }
}