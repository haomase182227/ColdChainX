using System.Collections.Concurrent;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Dapper;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options => { options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()); });
var app = builder.Build();
app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// In-memory state for multiple vehicles
var FleetState = new ConcurrentDictionary<string, VehicleSimulationState>();
var factory = new MqttFactory();

app.MapGet("/api/fleet/status", () =>
{
    return Results.Ok(FleetState.Values);
});

app.MapGet("/api/config", (IConfiguration config) => 
{
    return Results.Ok(new { GoongMapKey = config["GoongMapKey"] });
});

app.MapGet("/api/fleet/trips", async (IConfiguration config) =>
{
    try
    {
        var connStr = config.GetConnectionString("LocalConnection");
        using var conn = new NpgsqlConnection(connStr);
        var sql = @"
            SELECT 
                t.trip_id as ""TripId"", 
                t.status as ""Status"", 
                t.target_temperature as ""TargetTemperature"",
                i.device_code as ""DeviceCode""
            FROM master_trips t
            LEFT JOIN iot_devices i ON t.vehicle_id = i.vehicle_id
            ORDER BY t.created_at DESC 
            LIMIT 20";
        var trips = await conn.QueryAsync(sql);
        return Results.Ok(trips);
    }
    catch(Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapGet("/api/fleet/trip/{tripId}/polyline", async (string tripId, IConfiguration config) =>
{
    try
    {
        string? deviceCode = null;
        try 
        {
            var connStr = config.GetConnectionString("LocalConnection");
            using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            var sql = @"
                SELECT i.device_code
                FROM master_trips t
                JOIN vehicles v ON t.vehicle_id = v.vehicle_id
                JOIN iot_devices i ON v.vehicle_id = i.vehicle_id
                WHERE t.trip_id = @tripId";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("tripId", Guid.Parse(tripId));
            deviceCode = (await cmd.ExecuteScalarAsync())?.ToString();
        } 
        catch (Exception ex)
        {
            Console.WriteLine($"DB Error: {ex.Message}");
        }

        using var client = new HttpClient();
        var res = await client.GetAsync($"http://localhost:5000/api/dispatch/trip/{tripId}/route");
        if (!res.IsSuccessStatusCode) return Results.BadRequest("Cannot fetch from ColdChainX API");
        
        var json = await res.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("overviewPolyline", out var polylineElement))
        {
            var poly = polylineElement.GetString();
            if (string.IsNullOrEmpty(poly)) return Results.BadRequest("Polyline is empty");
            return Results.Ok(new { polyline = poly, deviceCode = deviceCode });
        }
        
        // Sometimes JSON serialization uses PascalCase or camelCase
        if (doc.RootElement.TryGetProperty("Data", out var dataCap) &&
            dataCap.TryGetProperty("OverviewPolyline", out var polylineElementCap))
        {
            return Results.Ok(new { polyline = polylineElementCap.GetString(), deviceCode = deviceCode });
        }

        return Results.BadRequest("Polyline not found in response.");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapPost("/api/fleet/start", async (SimulationRequest req, ILoggerFactory loggerFactory, IConfiguration config) =>
{
    var logger = loggerFactory.CreateLogger("Simulator");
    if (string.IsNullOrEmpty(req.Polyline)) return Results.BadRequest(new { error = "Polyline is required." });
    
    string deviceId = string.IsNullOrWhiteSpace(req.DeviceId) 
        ? $"SIM-TRUCK-{Guid.NewGuid().ToString().Substring(0,4)}" 
        : req.DeviceId;

    // Bổ sung yêu cầu: Chỉ chạy giả lập nếu thiết bị IoT thực tế (trong DB) đang ONLINE
    try
    {
        var connStr = config.GetConnectionString("LocalConnection");
        using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT \"IsOnline\" FROM iot_devices WHERE device_code = @deviceId";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        var res = await cmd.ExecuteScalarAsync();
        
        if (res == null || !(bool)res)
        {
            return Results.BadRequest(new { error = $"Thiết bị IoT '{deviceId}' hiện đang OFFLINE hoặc chưa phát dữ liệu thực tế. Vui lòng bật IoT thiết bị thực trước khi chạy giả lập!" });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error checking IoT status");
        return Results.BadRequest(new { error = "Lỗi khi kiểm tra trạng thái thiết bị IoT: " + ex.Message });
    }

    // Luôn luôn gửi lệnh tắt GPS 3 lớp khi bắt đầu mô phỏng (vì Simulator sẽ phát tọa độ)
    _ = SendMqttCommandAsync(deviceId, "DISABLE_GPS", logger);


    
    if (FleetState.ContainsKey(deviceId))
    {
        FleetState[deviceId].CancellationTokenSource?.Cancel();
    }
    
    var state = new VehicleSimulationState
    {
        DeviceId = deviceId,
        IsRunning = true,
        IsHybridMode = req.IsHybridMode,
        InjectTemp = req.InjectTemp,
        SpeedKmh = req.SpeedKmh > 0 ? req.SpeedKmh : 60,
        CurrentPointIndex = 0,
        TargetTemperature = req.TargetTemperature ?? -18.0,
        CancellationTokenSource = new CancellationTokenSource(),
        Path = DecodePolyline(req.Polyline)
    };
    
    if(state.Path.Count == 0) return Results.BadRequest("Invalid polyline.");
    
    state.CurrentLat = state.Path[0].Lat;
    state.CurrentLon = state.Path[0].Lon;
    
    FleetState[deviceId] = state;
    
    // Fire and forget background task for this vehicle
    _ = Task.Run(() => RunVehicleSimulation(state, logger), state.CancellationTokenSource.Token);
    
    return Results.Ok(new { deviceId = deviceId, points = state.Path.Count });
});

app.MapPost("/api/fleet/{deviceId}/stop", (string deviceId) =>
{
    if (FleetState.TryGetValue(deviceId, out var state))
    {
        state.CancellationTokenSource?.Cancel();
        state.IsRunning = false;
        return Results.Ok();
    }
    return Results.NotFound();
});

app.MapPost("/api/fleet/{deviceId}/anomaly", (string deviceId, AnomalyRequest req) =>
{
    if (FleetState.TryGetValue(deviceId, out var state))
    {
        if (req.Type == "TemperatureSpike")
        {
            state.TargetTemperature = req.Value ?? 15.0; // Break the fridge!
            state.InjectTemp = true; // Automatically enable temperature injection
        }
        else if (req.Type == "DoorOpen")
        {
            state.IsDoorOpen = true;
        }
        else if (req.Type == "DoorClose")
        {
            state.IsDoorOpen = false;
        }
        else if (req.Type == "FixTemperature")
        {
            state.TargetTemperature = -18.0; // Fix it
        }
        return Results.Ok(state);
    }
    return Results.NotFound();
});

app.MapPost("/api/fleet/{deviceId}/gps-source", (string deviceId, AnomalyRequest req, ILogger<Program> logger) =>
{
    if (FleetState.TryGetValue(deviceId, out var state))
    {
        bool wasReal = state.UseRealGps;
        state.UseRealGps = req.Value > 0;
        
        // Khi chuyển từ mạch thật về lại mô phỏng, ép nó nhảy về vị trí ảo trên lộ trình
        if (wasReal && !state.UseRealGps && state.Path != null && state.CurrentPointIndex < state.Path.Count)
        {
            state.CurrentLat = state.Path[state.CurrentPointIndex].Lat;
            state.CurrentLon = state.Path[state.CurrentPointIndex].Lon;
        }

        _ = SendMqttCommandAsync(deviceId, state.UseRealGps ? "ENABLE_GPS" : "DISABLE_GPS", logger);
        return Results.Ok();
    }
    return Results.NotFound();
});

app.MapPost("/api/fleet/{deviceId}/temp-source", (string deviceId, AnomalyRequest req) =>
{
    if (FleetState.TryGetValue(deviceId, out var state))
    {
        state.InjectTemp = req.Value <= 0; // Value > 0 means UseRealTemp, so InjectTemp = false
        return Results.Ok();
    }
    return Results.NotFound();
});

app.MapPost("/api/fleet/{deviceId}/speed", (string deviceId, AnomalyRequest req) =>
{
    if (FleetState.TryGetValue(deviceId, out var state))
    {
        state.SpeedKmh = req.Value ?? 60.0;
        return Results.Ok();
    }
    return Results.NotFound();
});

app.Run("http://*:5500");

// ==========================================
// BACKGROUND SIMULATION LOGIC
// ==========================================

static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
{
    var R = 6371d; 
    var dLat = (lat2 - lat1) * Math.PI / 180.0;
    var dLon = (lon2 - lon1) * Math.PI / 180.0;
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    return R * c;
}

static void InterpolatePosition(VehicleSimulationState state, double distanceToMoveKm)
{
    while (distanceToMoveKm > 0 && state.CurrentPointIndex < state.Path.Count - 1)
    {
        var p1Lat = state.CurrentLat;
        var p1Lon = state.CurrentLon;
        var p2 = state.Path[state.CurrentPointIndex + 1];
        
        double segmentDist = CalculateDistanceKm(p1Lat, p1Lon, p2.Lat, p2.Lon);
        
        if (segmentDist <= distanceToMoveKm)
        {
            distanceToMoveKm -= segmentDist;
            state.CurrentPointIndex++;
            state.CurrentLat = p2.Lat;
            state.CurrentLon = p2.Lon;
        }
        else
        {
            double ratio = distanceToMoveKm / segmentDist;
            state.CurrentLat = p1Lat + (p2.Lat - p1Lat) * ratio;
            state.CurrentLon = p1Lon + (p2.Lon - p1Lon) * ratio;
            distanceToMoveKm = 0; 
        }
    }
    
    if (state.CurrentPointIndex >= state.Path.Count - 1)
    {
        var lastPoint = state.Path.Last();
        state.CurrentLat = lastPoint.Lat;
        state.CurrentLon = lastPoint.Lon;
        state.CurrentPointIndex = state.Path.Count;
    }
}

async Task RunVehicleSimulation(VehicleSimulationState state, ILogger logger)
{
    var mqttClient = factory.CreateMqttClient();
    var options = new MqttClientOptionsBuilder()
        .WithTcpServer("8.231.129.222", 1883)
        .WithCredentials("esp32user", "183732")
        .WithClientId($"{state.DeviceId}{Guid.NewGuid().ToString().Substring(0,4)}")
        .Build();

    try
    {
        await mqttClient.ConnectAsync(options, state.CancellationTokenSource.Token);
        logger.LogInformation($"[{state.DeviceId}] Connected to MQTT. HybridMode={state.IsHybridMode}");
        
        if (state.IsHybridMode)
        {
            var rawTopic = $"telemetry/coldchain/{state.DeviceId}/raw";
            var dataTopic = $"telemetry/coldchain/{state.DeviceId}/data";
            DateTime lastMessageTime = DateTime.UtcNow;
            
            mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                if (e.ApplicationMessage.Topic == rawTopic || e.ApplicationMessage.Topic == dataTopic)
                {
                    var payloadStr = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(payloadStr);
                        var root = doc.RootElement;
                        
                        // Chống lặp vô hạn: Dùng cờ IsSimulated
                        if (root.TryGetProperty("IsSimulated", out var simEl) && simEl.GetBoolean() == true)
                        {
                            return; // Bỏ qua message do chính Simulator vừa gửi
                        }

                        if (state.InjectTemp) {
                            state.CurrentTemperature = state.TargetTemperature;
                        } else {
                            if (root.TryGetProperty("TempC", out var tempEl)) state.CurrentTemperature = tempEl.GetDouble();
                        }
                        
                        // XÓA: Bỏ ghi đè DoorOpen từ mạch IoT thật, để Simulator toàn quyền điều khiển cửa!
                        // if (root.TryGetProperty("DoorOpen", out var doorEl)) state.IsDoorOpen = doorEl.GetBoolean();
                        
                        if (state.UseRealGps) {
                            if (root.TryGetProperty("Lat", out var latEl)) state.CurrentLat = latEl.GetDouble();
                            if (root.TryGetProperty("Lon", out var lonEl)) state.CurrentLon = lonEl.GetDouble();
                        } else {
                            InterpolatePosition(state, (DateTime.UtcNow - lastMessageTime).TotalSeconds * (state.SpeedKmh / 3600.0));
                        }
                        lastMessageTime = DateTime.UtcNow;

                        var outObj = new
                        {
                            DeviceId = state.DeviceId,
                            TempC = state.CurrentTemperature,
                            DoorOpen = state.IsDoorOpen,
                            Lat = state.CurrentLat,
                            Lon = state.CurrentLon,
                            Timestamp = DateTime.UtcNow.ToString("O"),
                            IsSimulated = true
                        };

                        var outPayload = System.Text.Json.JsonSerializer.Serialize(outObj);
                        var msg = new MqttApplicationMessageBuilder()
                            .WithTopic(dataTopic)
                            .WithPayload(outPayload)
                            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                            .Build();
                        
                        await mqttClient.PublishAsync(msg);

                        var logMsg = $"[{state.DeviceId} HYBRID] Forwarded: {state.CurrentLat},{state.CurrentLon}";
                        if (state.InjectTemp) logMsg += $" InjectedTemp:{state.CurrentTemperature}C";
                        logger.LogInformation(logMsg);
                        
                        if (state.CurrentPointIndex >= state.Path.Count)
                        {
                            state.CancellationTokenSource?.Cancel();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"[HYBRID ERROR] {ex.Message}");
                    }
                }
            };
            
            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(rawTopic).Build());
            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(dataTopic).Build()); // Fallback if user hasn't flashed new code yet
            
            try { await Task.Delay(Timeout.Infinite, state.CancellationTokenSource.Token); } catch { }
        }
        else
        {
            var rnd = new Random();
            int tickDelayMs = 10000; 

            while (state.CurrentPointIndex < state.Path.Count && !state.CancellationTokenSource.Token.IsCancellationRequested)
            {
                double temp = state.TargetTemperature + (rnd.NextDouble() * 1.0 - 0.5);
                state.CurrentTemperature = Math.Round(temp, 1);

                var payload = new
                {
                    DeviceId = state.DeviceId,
                    TempC = state.CurrentTemperature,
                    DoorOpen = state.IsDoorOpen,
                    Lat = state.CurrentLat,
                    Lon = state.CurrentLon,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz")
                };

                string json = JsonSerializer.Serialize(payload);
                string topic = $"telemetry/coldchain/{state.DeviceId}/data";
                
                var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(json)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await mqttClient.PublishAsync(msg, state.CancellationTokenSource.Token);
                logger.LogInformation($"[{state.DeviceId}] Published: {state.CurrentLat},{state.CurrentLon} Temp:{state.CurrentTemperature}C");
                
                double distanceKm = (state.SpeedKmh / 3600.0) * (tickDelayMs / 1000.0);
                InterpolatePosition(state, distanceKm);
                
                await Task.Delay(tickDelayMs, state.CancellationTokenSource.Token);
            }
        }
        
        state.IsRunning = false;
        await mqttClient.DisconnectAsync();
    }
    catch (TaskCanceledException)
    {
        logger.LogInformation($"[{state.DeviceId}] Simulation stopped.");
    }
    catch (Exception ex)
    {
        logger.LogError($"[{state.DeviceId}] Error: {ex.Message}");
    }
    finally
    {
        state.IsRunning = false;
    }
}

// ==========================================
// POLYLINE DECODER ALGORITHM
// ==========================================
static List<Coordinate> DecodePolyline(string encodedPoints)
{
    if (string.IsNullOrEmpty(encodedPoints))
        return new List<Coordinate>();

    var polylineChars = encodedPoints.ToCharArray();
    int index = 0, currentLat = 0, currentLng = 0;
    var coordinates = new List<Coordinate>();

    while (index < polylineChars.Length)
    {
        int sum = 0, shifter = 0, next5Bits;
        do
        {
            next5Bits = polylineChars[index++] - 63;
            sum |= (next5Bits & 31) << shifter;
            shifter += 5;
        } while (next5Bits >= 32 && index < polylineChars.Length);
        if (index >= polylineChars.Length && next5Bits >= 32) break;
        currentLat += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

        sum = 0;
        shifter = 0;
        do
        {
            next5Bits = polylineChars[index++] - 63;
            sum |= (next5Bits & 31) << shifter;
            shifter += 5;
        } while (next5Bits >= 32 && index < polylineChars.Length);
        if (index >= polylineChars.Length && next5Bits >= 32) break;
        currentLng += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

        coordinates.Add(new Coordinate(currentLat / 1E5, currentLng / 1E5));
    }
    return coordinates;
}

async Task SendMqttCommandAsync(string deviceId, string action, ILogger logger)
{
    var cmdClient = factory.CreateMqttClient();
    var options = new MqttClientOptionsBuilder()
        .WithTcpServer("8.231.129.222", 1883)
        .WithCredentials("esp32user", "183732")
        .Build();
        
    try
    {
        await cmdClient.ConnectAsync(options);
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic($"command/coldchain/{deviceId}")
            .WithPayload($"{{\"action\":\"{action}\"}}")
            .Build();
        await cmdClient.PublishAsync(msg);
        await cmdClient.DisconnectAsync();
        logger.LogInformation($"Sent MQTT command {action} to {deviceId}");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, $"Failed to send MQTT command {action} to {deviceId}");
    }
}

// ==========================================
// MODELS
// ==========================================
public class SimulationRequest
{
    public string? Polyline { get; set; }
    public string? DeviceId { get; set; }
    public double SpeedKmh { get; set; } = 60;
    public double? TargetTemperature { get; set; }
    public bool IsHybridMode { get; set; }
    public bool InjectTemp { get; set; }
}

public class AnomalyRequest
{
    public string? Type { get; set; } 
    public double? Value { get; set; }
}

public class Coordinate
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public Coordinate(double lat, double lon) { Lat = lat; Lon = lon; }
}

public class VehicleSimulationState
{
    public string DeviceId { get; set; } = "";
    public bool IsRunning { get; set; }
    public bool IsHybridMode { get; set; }
    public bool InjectTemp { get; set; }
    public bool UseRealGps { get; set; }
    public double SpeedKmh { get; set; }
    public double CurrentLat { get; set; }
    public double CurrentLon { get; set; }
    public double CurrentTemperature { get; set; }
    public double TargetTemperature { get; set; }
    public bool IsDoorOpen { get; set; }

    public int CurrentPointIndex { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public List<Coordinate> Path { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}


