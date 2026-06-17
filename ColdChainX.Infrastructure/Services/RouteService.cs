using System.Globalization;
using System.Text;
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

        private static RouteOptionResponse ToResponse(RouteMaster route)
        {
            return new RouteOptionResponse
            {
                RouteId = route.RouteId,
                RouteCode = route.RouteCode,
                OriginCity = route.OriginCity,
                DestCity = route.DestCity,
                TransitTime = route.TransitTime,
                CutOffTime = route.CutOffTime,
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
