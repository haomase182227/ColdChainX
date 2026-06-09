using System.Globalization;
using System.Text.Json;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Services
{
    public class GoongLocationService : ILocationService
    {
        public const decimal HubLat = 10.732537m;
        public const decimal HubLon = 106.714447m;

        private readonly HttpClient _httpClient;

        public GoongLocationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText)
        {
            var apiKey = GetApiKey();
            var requestUri = $"Geocode?address={Uri.EscapeDataString(addressText.Trim())}&api_key={Uri.EscapeDataString(apiKey)}";

            using var response = await _httpClient.GetAsync(requestUri);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Goong geocode request failed: {content}");

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                throw new InvalidOperationException("Goong could not resolve destination address");

            var location = results[0].GetProperty("geometry").GetProperty("location");
            return (location.GetProperty("lat").GetDecimal(), location.GetProperty("lng").GetDecimal());
        }

        public async Task<decimal> GetDistanceKmAsync(decimal originLat, decimal originLon, decimal destinationLat, decimal destinationLon)
        {
            var apiKey = GetApiKey();
            var origin = FormatCoordinate(originLat, originLon);
            var destination = FormatCoordinate(destinationLat, destinationLon);
            var requestUri = $"DistanceMatrix?origins={origin}&destinations={destination}&vehicle=car&api_key={Uri.EscapeDataString(apiKey)}";

            using var response = await _httpClient.GetAsync(requestUri);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Goong distance matrix request failed: {content}");

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (!root.TryGetProperty("rows", out var rows) || rows.GetArrayLength() == 0)
                throw new InvalidOperationException("Goong could not calculate route distance");

            var elements = rows[0].GetProperty("elements");
            if (elements.GetArrayLength() == 0)
                throw new InvalidOperationException("Goong returned an empty distance result");

            var element = elements[0];
            if (element.TryGetProperty("status", out var status)
                && !string.Equals(status.GetString(), "OK", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Goong route distance status is {status.GetString()}");
            }

            var distanceMeters = element.GetProperty("distance").GetProperty("value").GetDecimal();
            return Math.Round(distanceMeters / 1000m, 2);
        }

        private static string GetApiKey()
        {
            var apiKey = Environment.GetEnvironmentVariable("key");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Goong API key is missing. Set key in .env");

            return apiKey.Trim();
        }

        private static string FormatCoordinate(decimal lat, decimal lon)
            => string.Create(CultureInfo.InvariantCulture, $"{lat},{lon}");
    }
}
