using ColdChainX.Application.DTOs.Routes;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IRouteService
    {
        Task<ApiResponse<IReadOnlyCollection<RouteOptionResponse>>> GetRouteOptionsAsync(string? originCity, string? destCity, string? status = null);
        Task<ApiResponse<RouteOptionResponse>> GetRouteByIdAsync(Guid routeId);
        Task<ApiResponse<RouteBookingOptionsDto>> GetRouteBookingOptionsAsync(Guid routeId);
        Task<ApiResponse<IReadOnlyCollection<WarehouseOptionDto>>> GetRouteOriginWarehousesAsync(Guid routeId);
        Task<ApiResponse<RouteOptionResponse>> CreateRouteAsync(CreateRouteRequest request);
        Task<ApiResponse<RouteOptionResponse>> UpdateRouteAsync(Guid routeId, UpdateRouteRequest request);
        Task<ApiResponse<bool>> DeleteRouteAsync(Guid routeId);

        // Schedules
        Task<ApiResponse<PagedResult<RouteScheduleDto>>> GetRouteSchedulesAsync(Guid routeId, int pageIndex, int pageSize);
        Task<ApiResponse<RouteScheduleDto>> AddRouteScheduleAsync(Guid routeId, CreateRouteScheduleRequest request);
        Task<ApiResponse<RouteScheduleDto>> UpdateRouteScheduleAsync(Guid routeId, Guid scheduleId, UpdateRouteScheduleRequest request);
        Task<ApiResponse<bool>> DeleteRouteScheduleAsync(Guid routeId, Guid scheduleId);

        // Stops
        Task<ApiResponse<PagedResult<RouteStopDto>>> GetRouteStopsAsync(Guid routeId, int pageIndex, int pageSize);
        Task<ApiResponse<RouteStopDto>> AddRouteStopAsync(Guid routeId, CreateRouteStopRequest request);
        Task<ApiResponse<RouteStopDto>> UpdateRouteStopAsync(Guid routeId, Guid stopId, UpdateRouteStopRequest request);
        Task<ApiResponse<bool>> DeleteRouteStopAsync(Guid routeId, Guid stopId);
    }
}
