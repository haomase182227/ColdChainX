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

        public async Task<ApiResponse<IReadOnlyCollection<RouteOptionResponse>>> GetRouteOptionsAsync(string? originCity, string? destCity, string? status = null)
        {
            var query = _db.RouteMasters.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status.ToUpper());
            }

            var routes = await query
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

            var vietnamNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
            var today = vietnamNow.Date;
            var currentTime = vietnamNow.TimeOfDay;

            var schedules = await _db.RouteSchedules
                .AsNoTracking()
                .Where(s => s.RouteId == routeId
                    && s.Status == "ACTIVE"
                    && (s.DepartureDate > today
                        || (s.DepartureDate == today && s.CutOffTime > currentTime)))
                .OrderBy(s => s.DepartureDate).ThenBy(s => s.DepartureTime)
                .Select(s => new ScheduleOptionDto
                {
                    ScheduleId = s.ScheduleId,
                    ScheduleName = s.ScheduleName,
                    DepartureDate = DateOnly.FromDateTime(s.DepartureDate),
                    DepartureTime = TimeOnly.FromTimeSpan(s.DepartureTime),
                    CutOffTime = TimeOnly.FromTimeSpan(s.CutOffTime),
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

        public async Task<ApiResponse<IReadOnlyCollection<WarehouseOptionDto>>> GetRouteOriginWarehousesAsync(Guid routeId)
        {
            var route = await _db.RouteMasters.FirstOrDefaultAsync(r => r.RouteId == routeId);
            if (route == null)
            {
                return ApiResponse<IReadOnlyCollection<WarehouseOptionDto>>.Failure("Route not found");
            }

            var originCity = route.OriginCity;
            
            // Find warehouses in the origin city
            var warehouses = await _db.Warehouses
                .Where(w => w.WarehouseName.Contains(originCity) || 
                            w.WarehouseCode.Contains(originCity) || 
                            (w.Address != null && w.Address.Contains(originCity)))
                .Select(w => new WarehouseOptionDto
                {
                    WarehouseId = w.WarehouseId,
                    WarehouseName = w.WarehouseName,
                    Address = w.Address
                })
                .ToListAsync();

            return ApiResponse<IReadOnlyCollection<WarehouseOptionDto>>.SuccessResponse(warehouses, "Warehouses retrieved successfully");
        }

        public async Task<ApiResponse<RouteOptionResponse>> GetRouteByIdAsync(Guid routeId)
        {
            var route = await _db.RouteMasters.AsNoTracking().FirstOrDefaultAsync(r => r.RouteId == routeId);
            if (route == null) return ApiResponse<RouteOptionResponse>.Failure("Route not found");

            return ApiResponse<RouteOptionResponse>.SuccessResponse(ToResponse(route), "Route retrieved successfully");
        }

        public async Task<ApiResponse<RouteOptionResponse>> CreateRouteAsync(CreateRouteRequest request)
        {
            var route = new RouteMaster
            {
                RouteId = Guid.NewGuid(),
                RouteCode = request.RouteCode,
                OriginCity = request.OriginCity,
                DestCity = request.DestCity,
                TransitTime = request.TransitTime,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };

            _db.RouteMasters.Add(route);
            await _db.SaveChangesAsync();

            return ApiResponse<RouteOptionResponse>.SuccessResponse(ToResponse(route), "Route created successfully");
        }

        public async Task<ApiResponse<RouteOptionResponse>> UpdateRouteAsync(Guid routeId, UpdateRouteRequest request)
        {
            var route = await _db.RouteMasters.FirstOrDefaultAsync(r => r.RouteId == routeId);
            if (route == null) return ApiResponse<RouteOptionResponse>.Failure("Route not found");

            route.RouteCode = request.RouteCode;
            route.OriginCity = request.OriginCity;
            route.DestCity = request.DestCity;
            route.TransitTime = request.TransitTime;
            route.Status = request.Status;

            await _db.SaveChangesAsync();
            return ApiResponse<RouteOptionResponse>.SuccessResponse(ToResponse(route), "Route updated successfully");
        }

        public async Task<ApiResponse<bool>> DeleteRouteAsync(Guid routeId)
        {
            var route = await _db.RouteMasters.FirstOrDefaultAsync(r => r.RouteId == routeId);
            if (route == null) return ApiResponse<bool>.Failure("Route not found");

            _db.RouteMasters.Remove(route);
            await _db.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Route deleted successfully");
        }

        public async Task<ApiResponse<PagedResult<RouteScheduleDto>>> GetRouteSchedulesAsync(Guid routeId, int pageIndex, int pageSize)
        {
            var query = _db.RouteSchedules.AsNoTracking().Where(s => s.RouteId == routeId);
            var totalCount = await query.CountAsync();

            var items = await query
                .OrderBy(s => s.DepartureDate)
                .ThenBy(s => s.DepartureTime)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new RouteScheduleDto
                {
                    ScheduleId = s.ScheduleId,
                    RouteId = s.RouteId,
                    ScheduleName = s.ScheduleName,
                    DepartureDate = DateOnly.FromDateTime(s.DepartureDate),
                    DepartureTime = TimeOnly.FromTimeSpan(s.DepartureTime),
                    CutOffTime = TimeOnly.FromTimeSpan(s.CutOffTime),
                    Status = s.Status,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();

            var pagedResult = PagedResult<RouteScheduleDto>.Create(items, totalCount, pageIndex, pageSize);
            return ApiResponse<PagedResult<RouteScheduleDto>>.SuccessResponse(pagedResult, "Route schedules retrieved successfully");
        }

        public async Task<ApiResponse<RouteScheduleDto>> AddRouteScheduleAsync(Guid routeId, CreateRouteScheduleRequest request)
        {
            var route = await _db.RouteMasters.FirstOrDefaultAsync(r => r.RouteId == routeId);
            if (route == null)
                return ApiResponse<RouteScheduleDto>.Failure("Route not found");

            var entity = new RouteSchedule
            {
                ScheduleId = Guid.NewGuid(),
                RouteId = routeId,
                ScheduleName = $"{route.RouteCode} ({GetVietnameseDayOfWeek(request.DepartureDate.ToDateTime(TimeOnly.MinValue))})",
                DepartureDate = request.DepartureDate.ToDateTime(TimeOnly.MinValue),
                DepartureTime = request.DepartureTime.ToTimeSpan(),
                CutOffTime = request.CutOffTime.ToTimeSpan(),
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
                DepartureDate = DateOnly.FromDateTime(entity.DepartureDate),
                DepartureTime = TimeOnly.FromTimeSpan(entity.DepartureTime),
                CutOffTime = TimeOnly.FromTimeSpan(entity.CutOffTime),
                Status = entity.Status,
                CreatedAt = entity.CreatedAt
            };

            return ApiResponse<RouteScheduleDto>.SuccessResponse(dto, "Route schedule added successfully");
        }

        public async Task<ApiResponse<RouteScheduleDto>> UpdateRouteScheduleAsync(Guid routeId, Guid scheduleId, UpdateRouteScheduleRequest request)
        {
            var route = await _db.RouteMasters.FirstOrDefaultAsync(r => r.RouteId == routeId);
            if (route == null) return ApiResponse<RouteScheduleDto>.Failure("Route not found");

            var entity = await _db.RouteSchedules.FirstOrDefaultAsync(s => s.RouteId == routeId && s.ScheduleId == scheduleId);
            if (entity == null) return ApiResponse<RouteScheduleDto>.Failure("Route schedule not found");

            var normalizedStatus = request.Status?.Trim().ToUpperInvariant();
            if (normalizedStatus is not ("ACTIVE" or "INACTIVE"))
                return ApiResponse<RouteScheduleDto>.Failure("Schedule status must be ACTIVE or INACTIVE");

            entity.ScheduleName = $"{route.RouteCode} ({GetVietnameseDayOfWeek(request.DepartureDate.ToDateTime(TimeOnly.MinValue))})";
            entity.DepartureDate = request.DepartureDate.ToDateTime(TimeOnly.MinValue);
            entity.DepartureTime = request.DepartureTime.ToTimeSpan();
            entity.CutOffTime = request.CutOffTime.ToTimeSpan();
            entity.Status = normalizedStatus;

            await _db.SaveChangesAsync();

            var dto = new RouteScheduleDto
            {
                ScheduleId = entity.ScheduleId,
                RouteId = entity.RouteId,
                ScheduleName = entity.ScheduleName,
                DepartureDate = DateOnly.FromDateTime(entity.DepartureDate),
                DepartureTime = TimeOnly.FromTimeSpan(entity.DepartureTime),
                CutOffTime = TimeOnly.FromTimeSpan(entity.CutOffTime),
                Status = entity.Status,
                CreatedAt = entity.CreatedAt
            };

            return ApiResponse<RouteScheduleDto>.SuccessResponse(dto, "Route schedule updated successfully");
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

        public async Task<ApiResponse<RouteStopDto>> UpdateRouteStopAsync(Guid routeId, Guid stopId, UpdateRouteStopRequest request)
        {
            var entity = await _db.RouteStops.FirstOrDefaultAsync(s => s.RouteId == routeId && s.StopId == stopId);
            if (entity == null) return ApiResponse<RouteStopDto>.Failure("Route stop not found");

            entity.StopName = request.StopName;
            await _db.SaveChangesAsync();

            var dto = new RouteStopDto
            {
                StopId = entity.StopId,
                RouteId = entity.RouteId,
                StopName = entity.StopName,
                CreatedAt = entity.CreatedAt
            };

            return ApiResponse<RouteStopDto>.SuccessResponse(dto, "Route stop updated successfully");
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
                Status = route.Status,
                CreatedAt = route.CreatedAt
            };
        }

        private string GetVietnameseDayOfWeek(DateTime date)
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Monday => "T2",
                DayOfWeek.Tuesday => "T3",
                DayOfWeek.Wednesday => "T4",
                DayOfWeek.Thursday => "T5",
                DayOfWeek.Friday => "T6",
                DayOfWeek.Saturday => "T7",
                DayOfWeek.Sunday => "CN",
                _ => ""
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
