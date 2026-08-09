using ColdChainX.Application.DTOs.Dispatch;

namespace ColdChainX.Application.Interfaces
{
    public interface ILocationService
    {
        Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText);
        Task<decimal> GetDistanceKmAsync(decimal originLat, decimal originLon, decimal destinationLat, decimal destinationLon);

        Task<GoongDirectionsResult> GetDirectionsAsync(List<(decimal Lat, decimal Lon, string Address)> waypoints);
    }
}
