using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Interfaces;

public interface IMqttCommandPublisher
{
    Task ActivateSirenAsync(string deviceCode, object reason, CancellationToken cancellationToken);
    Task<bool> StartStreamingAsync(string deviceCode, CancellationToken cancellationToken);
}
