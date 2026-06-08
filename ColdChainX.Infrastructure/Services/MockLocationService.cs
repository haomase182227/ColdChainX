using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Services
{
    public class MockLocationService : ILocationService
    {
        public const decimal HubLat = 10.732537m;
        public const decimal HubLon = 106.714447m;

        public Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText)
        {
            var normalized = addressText.Trim().ToLowerInvariant();

            var coordinates = normalized switch
            {
                var value when value.Contains("quận 1") || value.Contains("quan 1") => (10.7769m, 106.7009m),
                var value when value.Contains("quận 7") || value.Contains("quan 7") => (10.7356m, 106.7220m),
                var value when value.Contains("thủ đức") || value.Contains("thu duc") => (10.8494m, 106.7537m),
                var value when value.Contains("bình thạnh") || value.Contains("binh thanh") => (10.8118m, 106.7090m),
                _ => (10.7769m, 106.7009m)
            };

            return Task.FromResult(coordinates);
        }

        public decimal CalculateDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
        {
            const double earthRadiusKm = 6371;

            var dLat = ToRadians((double)(lat2 - lat1));
            var dLon = ToRadians((double)(lon2 - lon1));
            var originLat = ToRadians((double)lat1);
            var destLat = ToRadians((double)lat2);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                    + Math.Cos(originLat) * Math.Cos(destLat)
                    * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return Math.Round((decimal)(earthRadiusKm * c), 2);
        }

        private static double ToRadians(double degrees)
            => degrees * Math.PI / 180;
    }
}
