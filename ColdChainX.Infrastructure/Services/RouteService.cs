using System.Globalization;
using System.Text;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Routes;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services
{
    public class RouteService : IRouteService
    {
        private readonly ApplicationDbContext _db;

        public RouteService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<IReadOnlyCollection<RouteOptionResponse>>> GetRouteOptionsAsync(string? originCity, string? destCity)
        {
            var routes = await _db.RouteMasters
                .AsNoTracking()
                .Where(r => r.Status == "ACTIVE")
                .OrderBy(r => r.OriginCity)
                .ThenBy(r => r.DestCity)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(originCity))
            {
                var originKey = NormalizeRouteKey(originCity);
                routes = routes.Where(r => NormalizeRouteKey(r.OriginCity) == originKey).ToList();
            }

            if (!string.IsNullOrWhiteSpace(destCity))
            {
                var destKey = NormalizeRouteKey(destCity);
                routes = routes.Where(r => NormalizeRouteKey(r.DestCity) == destKey).ToList();
            }

            var data = routes.Select(ToResponse).ToList();
            return ApiResponse<IReadOnlyCollection<RouteOptionResponse>>.SuccessResponse(data, "Route options retrieved successfully");
        }

        public async Task<ApiResponse<RouteBookingOptionsDto>> GetRouteBookingOptionsAsync(Guid routeId)
        {
            var exists = await _db.RouteMasters.AnyAsync(r => r.RouteId == routeId);
            if (!exists)
            {
                return ApiResponse<RouteBookingOptionsDto>.Failure("Route not found", 404);
            }

            var schedules = await _db.RouteSchedules
                .AsNoTracking()
                .Where(s => s.RouteId == routeId && s.Status == "ACTIVE")
                .OrderBy(s => s.DayOfWeek).ThenBy(s => s.DepartureTime)
                .Select(s => new ScheduleOptionDto
                {
                    ScheduleId = s.ScheduleId,
                    ScheduleName = s.ScheduleName,
                    DayOfWeek = s.DayOfWeek,
                    DepartureTime = s.DepartureTime,
                    CutOffTime = s.CutOffTime
                })
                .ToListAsync();

            var stops = await _db.RouteStops
                .AsNoTracking()
                .Where(s => s.RouteId == routeId)
                .OrderBy(s => s.CreatedAt)
                .Select(s => new StopOptionDto
                {
                    StopId = s.StopId,
                    StopName = s.StopName
                })
                .ToListAsync();

            var result = new RouteBookingOptionsDto
            {
                RouteId = routeId,
                AvailableSchedules = schedules,
                AvailableStops = stops
            };

            return ApiResponse<RouteBookingOptionsDto>.SuccessResponse(result, "Booking options retrieved successfully");
        }

        public async Task<ApiResponse<PagedResult<RouteScheduleDto>>> GetRouteSchedulesAsync(Guid routeId, int pageIndex, int pageSize)
        {
            var query = _db.RouteSchedules.AsNoTracking().Where(s => s.RouteId == routeId);
            var totalCount = await query.CountAsync();

            var items = await query
                .OrderBy(s => s.DayOfWeek)
                .ThenBy(s => s.DepartureTime)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new RouteScheduleDto
                {
                    ScheduleId = s.ScheduleId,
                    RouteId = s.RouteId,
                    ScheduleName = s.ScheduleName,
                    DayOfWeek = s.DayOfWeek,
                    DepartureTime = s.DepartureTime,
                    CutOffTime = s.CutOffTime,
                    Status = s.Status,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();

            var pagedResult = PagedResult<RouteScheduleDto>.Create(items, totalCount, pageIndex, pageSize);
            return ApiResponse<PagedResult<RouteScheduleDto>>.SuccessResponse(pagedResult, "Route schedules retrieved successfully");
        }

        public async Task<ApiResponse<RouteScheduleDto>> AddRouteScheduleAsync(Guid routeId, CreateRouteScheduleRequest request)
        {
            if (!await _db.RouteMasters.AnyAsync(r => r.RouteId == routeId))
                return ApiResponse<RouteScheduleDto>.Failure("Route not found");

            var entity = new RouteSchedule
            {
                ScheduleId = Guid.NewGuid(),
                RouteId = routeId,
                ScheduleName = request.ScheduleName,
                DayOfWeek = request.DayOfWeek,
                DepartureTime = request.DepartureTime,
                CutOffTime = request.CutOffTime,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };

            _db.RouteSchedules.Add(entity);
            await _db.SaveChangesAsync();

            var dto = new RouteScheduleDto
            {
                ScheduleId = entity.ScheduleId,
                RouteId = entity.RouteId,
                ScheduleName = entity.ScheduleName,
                DayOfWeek = entity.DayOfWeek,
                DepartureTime = entity.DepartureTime,
                CutOffTime = entity.CutOffTime,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt
            };

            return ApiResponse<RouteScheduleDto>.SuccessResponse(dto, "Route schedule added successfully");
        }

        public async Task<ApiResponse<bool>> DeleteRouteScheduleAsync(Guid routeId, Guid scheduleId)
        {
            var entity = await _db.RouteSchedules.FirstOrDefaultAsync(s => s.RouteId == routeId && s.ScheduleId == scheduleId);
            if (entity == null)
                return ApiResponse<bool>.Failure("Route schedule not found");

            _db.RouteSchedules.Remove(entity);
            await _db.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Route schedule deleted successfully");
        }

        public async Task<ApiResponse<PagedResult<RouteStopDto>>> GetRouteStopsAsync(Guid routeId, int pageIndex, int pageSize)
        {
            var query = _db.RouteStops.AsNoTracking().Where(s => s.RouteId == routeId);
            var totalCount = await query.CountAsync();

            var items = await query
                .OrderBy(s => s.CreatedAt)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new RouteStopDto
                {
                    StopId = s.StopId,
                    RouteId = s.RouteId,
                    StopName = s.StopName,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();

            var pagedResult = PagedResult<RouteStopDto>.Create(items, totalCount, pageIndex, pageSize);
            return ApiResponse<PagedResult<RouteStopDto>>.SuccessResponse(pagedResult, "Route stops retrieved successfully");
        }

        public async Task<ApiResponse<RouteStopDto>> AddRouteStopAsync(Guid routeId, CreateRouteStopRequest request)
        {
            if (!await _db.RouteMasters.AnyAsync(r => r.RouteId == routeId))
                return ApiResponse<RouteStopDto>.Failure("Route not found");



            var entity = new RouteStop
            {
                StopId = Guid.NewGuid(),
                RouteId = routeId,
                StopName = request.StopName,
                CreatedAt = DateTime.UtcNow
            };

            _db.RouteStops.Add(entity);
            await _db.SaveChangesAsync();

            var dto = new RouteStopDto
            {
                StopId = entity.StopId,
                RouteId = entity.RouteId,
                StopName = entity.StopName,
                CreatedAt = entity.CreatedAt
            };

            return ApiResponse<RouteStopDto>.SuccessResponse(dto, "Route stop added successfully");
        }

        public async Task<ApiResponse<bool>> DeleteRouteStopAsync(Guid routeId, Guid stopId)
        {
            var entity = await _db.RouteStops.FirstOrDefaultAsync(s => s.RouteId == routeId && s.StopId == stopId);
            if (entity == null)
                return ApiResponse<bool>.Failure("Route stop not found");

            _db.RouteStops.Remove(entity);
            await _db.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Route stop deleted successfully");
        }

        private static RouteOptionResponse ToResponse(RouteMaster route)
        {
            return new RouteOptionResponse
            {
                RouteId = route.RouteId,
                RouteCode = route.RouteCode,
                OriginCity = route.OriginCity,
                DestCity = route.DestCity,
                TransitTime = route.TransitTime,
                Status = route.Status
            };
        }

        private static string NormalizeRouteKey(string? value)
        {
            return RemoveDiacritics(value ?? string.Empty)
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace(".", string.Empty)
                .Replace("-", string.Empty)
                .Replace("hochiminh", "hcm")
                .Replace("tphcm", "hcm")
                .Replace("saigon", "hcm");
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
