using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Workers;

/// <summary>
/// Background worker to periodically generate invoices for completed orders.
/// </summary>
public class AutoInvoicingWorker : BackgroundService
{
    private readonly ILogger<AutoInvoicingWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _period = TimeSpan.FromHours(24);

    public AutoInvoicingWorker(ILogger<AutoInvoicingWorker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoInvoicingWorker is starting.");

        using var timer = new PeriodicTimer(_period);
        
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("AutoInvoicingWorker running at: {time}", DateTimeOffset.Now);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
                    var result = await invoiceService.GeneratePeriodicInvoicesAsync();

                    if (result.Success)
                    {
                        _logger.LogInformation("AutoInvoicingWorker generated {count} invoices.", result.Data);
                    }
                    else
                    {
                        _logger.LogError("AutoInvoicingWorker failed: {message}", result.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in AutoInvoicingWorker.");
            }
        }

        _logger.LogInformation("AutoInvoicingWorker is stopping.");
    }
}
