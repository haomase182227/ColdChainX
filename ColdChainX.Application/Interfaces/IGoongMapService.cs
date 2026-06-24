using ColdChainX.Application.DTOs.Dispatch;

namespace ColdChainX.Application.Interfaces;

public interface IGoongMapService
{
    Task<GoongOptimizedRouteResult> GetOptimizedRouteAsync(
        string origin,
        string destination,
        string? waypoints,
        CancellationToken cancellationToken = default);
}
