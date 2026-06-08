namespace ColdChainX.Application.Interfaces
{
    public interface ILocationService
    {
        Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText);
        decimal CalculateDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2);
    }
}
