using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.SystemConfigs;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface ISystemConfigService
    {
        Task<ApiResponse<List<SystemConfigDto>>> GetAllConfigsAsync();
        Task<ApiResponse<SystemConfigDto>> GetConfigByKeyAsync(string key);
        Task<ApiResponse<SystemConfigDto>> UpdateConfigAsync(string key, UpdateSystemConfigRequest request);
    }
}
