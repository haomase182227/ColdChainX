using ColdChainX.Application.DTOs.Routes;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IRouteService
    {
        Task<ApiResponse<IReadOnlyCollection<RouteOptionResponse>>> GetRouteOptionsAsync(string? originCity, string? destCity);
        Task<ApiResponse<RouteBookingOptionsDto>> GetRouteBookingOptionsAsync(Guid routeId);

        // Schedules
        Task<PagedResponse<IReadOnlyCollection<RouteScheduleDto>>> GetRouteSchedulesAsync(Guid routeId, int pageIndex, int pageSize);
        Task<ApiResponse<RouteScheduleDto>> AddRouteScheduleAsync(Guid routeId, CreateRouteScheduleRequest request);
        Task<ApiResponse<bool>> DeleteRouteScheduleAsync(Guid routeId, Guid scheduleId);

        // Stops
        Task<PagedResponse<IReadOnlyCollection<RouteStopDto>>> GetRouteStopsAsync(Guid routeId, int pageIndex, int pageSize);
        Task<ApiResponse<RouteStopDto>> AddRouteStopAsync(Guid routeId, CreateRouteStopRequest request);
        Task<ApiResponse<bool>> DeleteRouteStopAsync(Guid routeId, Guid stopId);
    }
}
