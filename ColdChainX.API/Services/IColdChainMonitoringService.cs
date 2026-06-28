using ColdChainX.API.Models;

namespace ColdChainX.API.Services;

public interface IColdChainMonitoringService
{
    Task ProcessTelemetryAsync(TelemetryData data, CancellationToken cancellationToken);

    Task ProcessTelemetryBatchAsync(IReadOnlyCollection<TelemetryData> batch, CancellationToken cancellationToken);
}
