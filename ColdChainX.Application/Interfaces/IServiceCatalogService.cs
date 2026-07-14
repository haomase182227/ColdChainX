using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.ServiceCatalogs;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IServiceCatalogService
    {
        Task<ApiResponse<List<ServiceCatalogDto>>> GetAllAsync();
        Task<ApiResponse<List<ServiceCatalogDto>>> GetActiveAsync();
        Task<ApiResponse<ServiceCatalogDto>> GetByIdAsync(Guid id);
        Task<ApiResponse<ServiceCatalogDto>> CreateAsync(CreateServiceCatalogRequest request);
        Task<ApiResponse<ServiceCatalogDto>> UpdateAsync(Guid id, UpdateServiceCatalogRequest request);
        Task<ApiResponse<bool>> DeleteAsync(Guid id);
    }
}
