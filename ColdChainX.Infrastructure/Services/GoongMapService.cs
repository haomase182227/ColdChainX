using System.Globalization;
using System.Text;
using System.Text.Json;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Services;

public sealed class GoongMapService : IGoongMapService
{
    private readonly HttpClient _httpClient;

    public GoongMapService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GoongOptimizedRouteResult> GetOptimizedRouteAsync(
        string origin,
        string destination,
        string? waypoints,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new ArgumentException("Origin is required.", nameof(origin));
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Destination is required.", nameof(destination));
        }

        var apiKey = GetApiKey();
        var requestUri = new StringBuilder("Direction")
            .Append("?origin=").Append(Uri.EscapeDataString(origin))
            .Append("&destination=").Append(Uri.EscapeDataString(destination))
            .Append("&vehicle=car")
            .Append("&optimize=true");

        if (!string.IsNullOrWhiteSpace(waypoints))
        {
            requestUri.Append("&waypoints=").Append(Uri.EscapeDataString(waypoints));
        }

        requestUri.Append("&api_key=").Append(Uri.EscapeDataString(apiKey));

        using var response = await _httpClient.GetAsync(requestUri.ToString(), cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Goong optimized route request failed: {content}");
        }

        using var document = JsonDocument.Parse(content);
        return ParseRoute(document.RootElement);
    }

    private static GoongOptimizedRouteResult ParseRoute(JsonElement root)
    {
        if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Goong Direction API returned no routes.");
        }

        var route = routes[0];
        var totalDistanceMeters = 0;
        var totalDurationSeconds = 0;

        if (route.TryGetProperty("legs", out var legs))
        {
            foreach (var leg in legs.EnumerateArray())
            {
                totalDistanceMeters += ReadNestedInt(leg, "distance", "value");
                totalDurationSeconds += ReadNestedInt(leg, "duration", "value");
            }
        }

        return new GoongOptimizedRouteResult
        {
            OverviewPolyline = ReadOverviewPolyline(route),
            TotalDistanceMeters = totalDistanceMeters,
            TotalDurationSeconds = totalDurationSeconds,
            WaypointOrder = ReadWaypointOrder(root, route)
        };
    }

    private static string? ReadOverviewPolyline(JsonElement route)
    {
        if (route.TryGetProperty("overview_polyline", out var overview)
            && overview.TryGetProperty("points", out var points))
        {
            return points.GetString();
        }

        return null;
    }

    private static int ReadNestedInt(JsonElement element, string objectName, string propertyName)
    {
        if (element.TryGetProperty(objectName, out var obj)
            && obj.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return 0;
    }

    private static IReadOnlyList<int> ReadWaypointOrder(JsonElement root, JsonElement route)
    {
        if (route.TryGetProperty("waypoint_order", out var routeOrder))
        {
            return ReadIntArray(routeOrder);
        }

        if (root.TryGetProperty("waypoint_order", out var rootOrder))
        {
            return ReadIntArray(rootOrder);
        }

        return Array.Empty<int>();
    }

    private static IReadOnlyList<int> ReadIntArray(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        return array.EnumerateArray()
            .Where(item => item.TryGetInt32(out _))
            .Select(item => item.GetInt32())
            .ToArray();
    }

    private static string GetApiKey()
    {
        var apiKey = Environment.GetEnvironmentVariable("key");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Goong API key is missing. Set key in .env");
        }

        return apiKey.Trim();
    }

    public static string FormatCoordinate(decimal lat, decimal lon)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{lat},{lon}");
    }
}
