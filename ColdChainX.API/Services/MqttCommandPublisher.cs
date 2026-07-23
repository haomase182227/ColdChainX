using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Services;

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

    public async Task<bool> StartStreamingAsync(string deviceCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return false;
        }

        var host = _configuration["Mqtt:Host"] ?? "localhost";
        var port = _configuration.GetValue("Mqtt:Port", 1883);
        var username = _configuration["Mqtt:Username"];
        var topicPrefix = _configuration["Mqtt:CommandTopicPrefix"] ?? "command/coldchain";

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"coldchainx-command-stream-{Guid.NewGuid():N}")
            .WithTcpServer(host, port)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(username))
        {
            optionsBuilder.WithCredentials(username, _configuration["Mqtt:Password"]);
        }

        var payload = JsonSerializer.Serialize(new
        {
            command = "START_STREAMING",
            deviceCode = deviceCode,
            timestamp = DateTimeOffset.UtcNow
        });

        // The master prompt specified the topic `command/coldchain/{deviceCode}`
        var topic = $"command/coldchain/{deviceCode}";

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var client = new MqttFactory().CreateMqttClient();
        try
        {
            await client.ConnectAsync(optionsBuilder.Build(), cancellationToken);
            await client.PublishAsync(message, cancellationToken);
            _logger.LogInformation("Successfully published START_STREAMING command to {DeviceCode}", deviceCode);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish start streaming command to device {DeviceCode}.", deviceCode);
            return false;
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
