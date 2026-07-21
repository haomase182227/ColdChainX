using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Workers;

public class FleetComplianceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FleetComplianceWorker> _logger;

    public FleetComplianceWorker(IServiceScopeFactory scopeFactory, ILogger<FleetComplianceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[FleetCompliance] Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("[FleetCompliance] Scan started at {Time}.", DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy"));
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IFleetManagementService>();
                await service.RunComplianceScanAsync(stoppingToken);
                _logger.LogInformation("[FleetCompliance] Scan completed successfully.");

                var now = DateTime.Now;
                var nextRun = now.Date.AddDays(1);
                var delay = nextRun - now;
                _logger.LogInformation("[FleetCompliance] Next scan scheduled at {Time} (in {Hours:0.##} hours).",
                    nextRun.ToString("HH:mm:ss dd/MM/yyyy"), delay.TotalHours);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FleetCompliance] Scan failed. Retrying in 1 hour.");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        _logger.LogInformation("[FleetCompliance] Worker stopped.");
    }
}
