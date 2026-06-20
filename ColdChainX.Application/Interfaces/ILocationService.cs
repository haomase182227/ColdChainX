using ColdChainX.Application.DTOs.Dispatch;

namespace ColdChainX.Application.Interfaces
{
    public interface ILocationService
    {
        Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText);
        Task<decimal> GetDistanceKmAsync(decimal originLat, decimal originLon, decimal destinationLat, decimal destinationLon);

        /// <summary>
        /// Gọi Goong Directions API để lấy hướng dẫn đường đi (turn-by-turn) qua danh sách waypoints.
        /// Waypoints[0] = origin, Waypoints[last] = destination, giữa là các điểm dừng.
        /// </summary>
        Task<GoongDirectionsResult> GetDirectionsAsync(List<(decimal Lat, decimal Lon, string Address)> waypoints);
    }
}
