namespace ColdChainX.Application.Interfaces
{
    public interface ILocationService
    {
        Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText);
        Task<decimal> GetDistanceKmAsync(decimal originLat, decimal originLon, decimal destinationLat, decimal destinationLon);
    }
}
