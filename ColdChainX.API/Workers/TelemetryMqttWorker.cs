using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ColdChainX.API.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace ColdChainX.API.Workers;

public sealed class TelemetryMqttWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Channel<TelemetryData> _telemetryChannel;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelemetryMqttWorker> _logger;
    private readonly IMqttClient _mqttClient;

    public TelemetryMqttWorker(
        Channel<TelemetryData> telemetryChannel,
        IConfiguration configuration,
        ILogger<TelemetryMqttWorker> logger)
    {
        _telemetryChannel = telemetryChannel;
        _configuration = configuration;
        _logger = logger;

        _mqttClient = new MqttFactory().CreateMqttClient();
        _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageReceivedAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = _configuration["Mqtt:Host"] ?? "localhost";
        var port = _configuration.GetValue("Mqtt:Port", 1883);
        var clientId = _configuration["Mqtt:ClientId"] ?? $"coldchainx-api-{Guid.NewGuid():N}";
        var topic = _configuration["Mqtt:Topic"] ?? "telemetry/coldchain/#";

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(host, port)
            .WithCleanSession();

        var username = _configuration["Mqtt:Username"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            optionsBuilder.WithCredentials(username, _configuration["Mqtt:Password"]);
        }

        var options = optionsBuilder.Build();
        var topicFilter = new MqttTopicFilterBuilder()
            .WithTopic(topic)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    await _mqttClient.ConnectAsync(options, stoppingToken);
                    await _mqttClient.SubscribeAsync(topicFilter, stoppingToken);

                    _logger.LogInformation(
                        "Telemetry MQTT worker subscribed to {Topic} on {Host}:{Port}",
                        topic,
                        host,
                        port);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telemetry MQTT worker connection failed. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment.ToArray());

        try
        {
            var telemetry = JsonSerializer.Deserialize<TelemetryData>(payload, JsonOptions);
            if (telemetry is null || string.IsNullOrWhiteSpace(telemetry.DeviceId))
            {
                _logger.LogWarning("Invalid telemetry payload on {Topic}: {Payload}", args.ApplicationMessage.Topic, payload);
                return;
            }

            await _telemetryChannel.Writer.WriteAsync(telemetry);

            _logger.LogInformation(
                "Telemetry received topic={Topic} device={DeviceId} temp={TempC} doorOpen={DoorOpen} timestamp={Timestamp}",
                args.ApplicationMessage.Topic,
                telemetry.DeviceId,
                telemetry.TempC,
                telemetry.DoorOpen,
                telemetry.Timestamp);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize telemetry payload on {Topic}: {Payload}", args.ApplicationMessage.Topic, payload);
        }
    }
}
