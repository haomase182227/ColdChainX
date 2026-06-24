using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace ColdChainX.API.Services;

public interface IMqttCommandPublisher
{
    Task ActivateSirenAsync(string deviceCode, object reason, CancellationToken cancellationToken);
}

public sealed class MqttCommandPublisher : IMqttCommandPublisher
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MqttCommandPublisher> _logger;

    public MqttCommandPublisher(IConfiguration configuration, ILogger<MqttCommandPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ActivateSirenAsync(string deviceCode, object reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return;
        }

        var host = _configuration["Mqtt:Host"] ?? "localhost";
        var port = _configuration.GetValue("Mqtt:Port", 1883);
        var username = _configuration["Mqtt:Username"];
        var topicPrefix = _configuration["Mqtt:CommandTopicPrefix"] ?? "commands/coldchain";

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"coldchainx-command-{Guid.NewGuid():N}")
            .WithTcpServer(host, port)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(username))
        {
            optionsBuilder.WithCredentials(username, _configuration["Mqtt:Password"]);
        }

        var payload = JsonSerializer.Serialize(new
        {
            Command = "ACTIVATE_SIREN",
            DeviceCode = deviceCode,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"{topicPrefix}/{deviceCode}/commands")
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var client = new MqttFactory().CreateMqttClient();
        try
        {
            await client.ConnectAsync(optionsBuilder.Build(), cancellationToken);
            await client.PublishAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish siren command to device {DeviceCode}.", deviceCode);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(cancellationToken: cancellationToken);
            }
        }
    }
}
