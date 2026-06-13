using ColdChainX.Application.DTOs.Routes;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IRouteService
    {
        Task<ApiResponse<IReadOnlyCollection<RouteOptionResponse>>> GetRouteOptionsAsync(string? originCity, string? destCity);
    }
}
