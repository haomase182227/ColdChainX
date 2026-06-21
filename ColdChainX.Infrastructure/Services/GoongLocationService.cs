using System.Globalization;
using System.Text.Json;
using ColdChainX.Application.DTOs.Dispatch;
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

        /// <summary>
        /// Gọi Goong Directions API để lấy hướng dẫn đường đi (turn-by-turn).
        /// Endpoint: GET /Direction?origin={lat,lng}&destination={lat,lng}&waypoints={lat,lng|lat,lng}&vehicle=car&api_key={}
        /// </summary>
        public async Task<GoongDirectionsResult> GetDirectionsAsync(
            List<(decimal Lat, decimal Lon, string Address)> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2)
                throw new ArgumentException("Cần ít nhất 2 waypoints (origin + destination) để tính hướng dẫn đường đi.");

            var apiKey = GetApiKey();
            var origin = FormatCoordinate(waypoints[0].Lat, waypoints[0].Lon);
            var destination = FormatCoordinate(waypoints[^1].Lat, waypoints[^1].Lon);

            // Build waypoints string (các điểm giữa, phân cách bằng |)
            var waypointsParam = "";
            if (waypoints.Count > 2)
            {
                var middlePoints = waypoints
                    .Skip(1)
                    .Take(waypoints.Count - 2)
                    .Select(w => FormatCoordinate(w.Lat, w.Lon));
                waypointsParam = $"&waypoints={string.Join("|", middlePoints)}";
            }

            var requestUri = $"Direction?origin={origin}&destination={destination}{waypointsParam}&vehicle=car&api_key={Uri.EscapeDataString(apiKey)}";

            try
            {
                using var response = await _httpClient.GetAsync(requestUri);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Goong Direction API request failed: {content}");

                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;

                if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                    throw new InvalidOperationException("Goong Direction API returned no routes.");

                var route = routes[0];

                // Parse overview polyline
                string? overviewPolyline = null;
                if (route.TryGetProperty("overview_polyline", out var polylineProp)
                    && polylineProp.TryGetProperty("points", out var pointsProp))
                {
                    overviewPolyline = pointsProp.GetString();
                }

                // Parse legs
                var legs = new List<GoongLeg>();
                var totalDistanceM = 0m;
                var totalDurationS = 0;

                if (route.TryGetProperty("legs", out var legsArray))
                {
                    for (int i = 0; i < legsArray.GetArrayLength(); i++)
                    {
                        var leg = legsArray[i];
                        var legDistanceM = leg.TryGetProperty("distance", out var legDist)
                            ? legDist.GetProperty("value").GetDecimal() : 0m;
                        var legDurationS = leg.TryGetProperty("duration", out var legDur)
                            ? legDur.GetProperty("value").GetInt32() : 0;

                        totalDistanceM += legDistanceM;
                        totalDurationS += legDurationS;

                        // Determine start/end addresses from waypoints
                        var startAddr = i < waypoints.Count ? waypoints[i].Address : "N/A";
                        var endAddr = (i + 1) < waypoints.Count ? waypoints[i + 1].Address : "N/A";

                        // Parse steps
                        var steps = new List<GoongStep>();
                        if (leg.TryGetProperty("steps", out var stepsArray))
                        {
                            for (int j = 0; j < stepsArray.GetArrayLength(); j++)
                            {
                                var step = stepsArray[j];
                                var stepDistM = step.TryGetProperty("distance", out var sDist)
                                    ? sDist.GetProperty("value").GetDecimal() : 0m;
                                var stepDurS = step.TryGetProperty("duration", out var sDur)
                                    ? sDur.GetProperty("value").GetInt32() : 0;
                                var instruction = step.TryGetProperty("html_instructions", out var htmlInst)
                                    ? StripHtml(htmlInst.GetString() ?? "") : "";
                                var maneuver = step.TryGetProperty("maneuver", out var man)
                                    ? man.GetString() : null;

                                steps.Add(new GoongStep
                                {
                                    Instruction = instruction,
                                    DistanceKm = Math.Round(stepDistM / 1000m, 2),
                                    DurationSeconds = stepDurS,
                                    Maneuver = maneuver
                                });
                            }
                        }

                        legs.Add(new GoongLeg
                        {
                            DistanceKm = Math.Round(legDistanceM / 1000m, 2),
                            DurationSeconds = legDurationS,
                            StartAddress = startAddr,
                            EndAddress = endAddr,
                            Steps = steps
                        });
                    }
                }

                return new GoongDirectionsResult
                {
                    TotalDistanceKm = Math.Round(totalDistanceM / 1000m, 2),
                    TotalDurationSeconds = totalDurationS,
                    OverviewPolyline = overviewPolyline,
                    Legs = legs
                };
            }
            catch (HttpRequestException)
            {
                // Fallback khi Goong API không khả dụng
                return BuildFallbackDirections(waypoints);
            }
        }

        /// <summary>Fallback navigation khi Goong Directions API không khả dụng.</summary>
        private static GoongDirectionsResult BuildFallbackDirections(
            List<(decimal Lat, decimal Lon, string Address)> waypoints)
        {
            var legs = new List<GoongLeg>();
            var totalDist = 0m;

            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                var distKm = HaversineKm(waypoints[i].Lat, waypoints[i].Lon,
                                          waypoints[i + 1].Lat, waypoints[i + 1].Lon);
                totalDist += distKm;
                var durationS = (int)(distKm / 40m * 3600m); // ~40 km/h trung bình nội thành

                legs.Add(new GoongLeg
                {
                    DistanceKm = distKm,
                    DurationSeconds = durationS,
                    StartAddress = waypoints[i].Address,
                    EndAddress = waypoints[i + 1].Address,
                    Steps = new List<GoongStep>
                    {
                        new()
                        {
                            Instruction = $"Di chuyển từ {waypoints[i].Address} đến {waypoints[i + 1].Address}",
                            DistanceKm = distKm,
                            DurationSeconds = durationS,
                            Maneuver = "straight"
                        }
                    }
                });
            }

            return new GoongDirectionsResult
            {
                TotalDistanceKm = Math.Round(totalDist, 2),
                TotalDurationSeconds = legs.Sum(l => l.DurationSeconds),
                OverviewPolyline = null,
                Legs = legs
            };
        }

        private static decimal HaversineKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
        {
            const double R = 6371.0;
            var dLat = ToRad((double)(lat2 - lat1));
            var dLon = ToRad((double)(lon2 - lon1));
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(ToRad((double)lat1)) * Math.Cos(ToRad((double)lat2))
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (decimal)Math.Round(R * c, 2);
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Loại bỏ HTML tags khỏi Goong instruction text.</summary>
        private static string StripHtml(string html)
        {
            return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ").Trim();
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

