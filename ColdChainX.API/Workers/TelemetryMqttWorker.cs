using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ColdChainX.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace ColdChainX.API.Workers;

public sealed class TelemetryMqttWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _activeConnections = new();

    // Track when a device last sent a simulated message
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _simulatedDevices = new();

    private readonly Channel<TelemetryData> _telemetryChannel;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelemetryMqttWorker> _logger;
    private readonly IMqttClient _mqttClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public TelemetryMqttWorker(
        Channel<TelemetryData> telemetryChannel,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryMqttWorker> logger)
    {
        _telemetryChannel = telemetryChannel;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
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
            .WithProtocolVersion(MqttProtocolVersion.V311)
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
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment.ToArray());

        if (topic.EndsWith("/status", StringComparison.OrdinalIgnoreCase))
        {
            await HandleStatusMessageAsync(topic, payload);
            return;
        }
        else if (topic.EndsWith("/data", StringComparison.OrdinalIgnoreCase) || topic.Contains("/telemetry/"))
        {
            await HandleDataMessageAsync(topic, payload);
            return;
        }
    }

    private async Task HandleStatusMessageAsync(string topic, string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("status", out var statusProp))
            {
                var statusStr = statusProp.GetString();
                bool isOnline = string.Equals(statusStr, "ONLINE", StringComparison.OrdinalIgnoreCase);
                
                string? incomingClientId = null;
                if (doc.RootElement.TryGetProperty("clientId", out var clientProp))
                {
                    incomingClientId = clientProp.GetString();
                }

                // Extract device code from topic, e.g. telemetry/coldchain/{deviceCode}/status
                var parts = topic.Split('/');
                if (parts.Length >= 2)
                {
                    var deviceCode = parts[parts.Length - 2];

                    if (isOnline)
                    {
                        if (!string.IsNullOrEmpty(incomingClientId))
                            _activeConnections[deviceCode] = incomingClientId;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(incomingClientId) && _activeConnections.TryGetValue(deviceCode, out var currentClientId))
                        {
                            if (currentClientId != incomingClientId)
                            {
                                _logger.LogInformation("Ignored old LWT OFFLINE for {DeviceCode} (Old: {OldId}, Current: {CurrentId})", deviceCode, incomingClientId, currentClientId);
                                return;
                            }
                        }
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ColdChainX.Infrastructure.Persistence.ApplicationDbContext>();
                    var device = await db.IotDevices.FirstOrDefaultAsync(d => d.DeviceCode == deviceCode);
                    if (device != null)
                    {
                        device.IsOnline = isOnline;
                        await db.SaveChangesAsync();
                        _logger.LogInformation("Updated device {DeviceCode} IsOnline to {IsOnline}", deviceCode, isOnline);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process status message on topic {Topic}", topic);
        }
    }

    private async Task HandleDataMessageAsync(string topic, string payload)
    {
        try
        {
            var telemetry = JsonSerializer.Deserialize<TelemetryData>(payload, JsonOptions);
            if (telemetry is null || string.IsNullOrWhiteSpace(telemetry.DeviceId))
            {
                _logger.LogWarning("Invalid telemetry payload on {Topic}: {Payload}", topic, payload);
                return;
            }

            // DEDUPLICATION LOGIC FOR HYBRID MODE
            // The Simulator forwards the message with IsSimulated = true.
            // The real ESP32 sends without IsSimulated.
            bool isSimulated = false;
            try 
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("IsSimulated", out var simEl) && simEl.ValueKind == JsonValueKind.True)
                {
                    isSimulated = true;
                }
            }
            catch { }

            if (isSimulated)
            {
                // Mark this device as currently being simulated
                _simulatedDevices[telemetry.DeviceId] = DateTime.UtcNow;
            }
            else
            {
                // Debounce: Wait 2 seconds to allow Simulator (Hybrid Mode) to intercept and send simulated=True
                await Task.Delay(2000);

                // If it's a real message, check if the simulator is active (sent a message in the last 5 seconds)
                if (_simulatedDevices.TryGetValue(telemetry.DeviceId, out var lastSimTime))
                {
                    if ((DateTime.UtcNow - lastSimTime).TotalSeconds < 5)
                    {
                        // Drop the real message because the simulator is overriding it right now
                        return;
                    }
                }
            }

            await _telemetryChannel.Writer.WriteAsync(telemetry);

            _logger.LogInformation(
                "Telemetry received topic={Topic} device={DeviceId} temp={TempC} doorOpen={DoorOpen} lat={Lat} lon={Lon} timestamp={Timestamp} simulated={IsSimulated}",
                topic,
                telemetry.DeviceId,
                telemetry.TempC,
                telemetry.DoorOpen,
                telemetry.Lat,
                telemetry.Lon,
                telemetry.Timestamp,
                isSimulated);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize telemetry payload on {Topic}: {Payload}", topic, payload);
        }
    }
}
