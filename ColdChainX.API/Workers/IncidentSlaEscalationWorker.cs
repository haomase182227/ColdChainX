using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Workers;

public sealed class IncidentSlaEscalationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IncidentSlaEscalationWorker> _logger;
    private readonly TimeSpan _scanInterval;

    public IncidentSlaEscalationWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<IncidentSlaEscalationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _scanInterval = TimeSpan.FromSeconds(Math.Max(
            30,
            configuration.GetValue<int?>("IncidentWorkflow:SlaScanIntervalSeconds") ?? 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_scanInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IIncidentReportService>();
                var escalated = await service.EscalateOverdueReportedIncidentsAsync(DateTime.UtcNow);
                if (escalated > 0)
                    _logger.LogWarning("Escalated {IncidentCount} overdue REPORTED incidents.", escalated);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Incident SLA escalation scan failed.");
            }
        }
    }
}
