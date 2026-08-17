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

var FleetState = new ConcurrentDictionary<string, VehicleSimulationState>();
var StandaloneDevices = new ConcurrentDictionary<string, StandaloneIotState>();
var factory = new MqttFactory();

app.MapGet("/api/iot/devices", async (IConfiguration config) =>
{
    try
    {
        var connStr = config.GetConnectionString("LocalConnection");
        using var conn = new NpgsqlConnection(connStr);
        try { await conn.ExecuteAsync("DELETE FROM iot_devices WHERE device_code LIKE 'SIM-TRUCK-%'"); } catch { }

        var sql = @"
            SELECT 
                d.device_id as ""DeviceId"",
                d.device_code as ""DeviceCode"",
                d.vehicle_id as ""VehicleId"",
                v.truck_plate as ""PlateNumber"",
                d.battery_level as ""BatteryLevel"",
                d.last_ping_time as ""LastPingTime"",
                d.status as ""Status"",
                d.""IsOnline"" as ""IsOnline""
            FROM iot_devices d
            LEFT JOIN vehicles v ON d.vehicle_id = v.vehicle_id
            WHERE d.device_code NOT LIKE 'SIM-TRUCK-%'
            ORDER BY d.""IsOnline"" DESC, d.last_ping_time DESC NULLS LAST, d.created_at DESC";
        var items = await conn.QueryAsync(sql);
        var devices = items.Select(d =>
        {
            var code = (string?)d.DeviceCode;
            var isSimRunning = code != null && StandaloneDevices.TryGetValue(code, out var std) && std.IsOnline;
            var isStreaming = code != null && StandaloneDevices.TryGetValue(code, out var std2) && std2.IsStreaming;

            return new
            {
                d.DeviceId,
                d.DeviceCode,
                d.VehicleId,
                d.PlateNumber,
                d.BatteryLevel,
                d.LastPingTime,
                d.Status,
                d.IsOnline,
                IsSimulatedOnline = isSimRunning,
                IsStreaming = isStreaming
            };
        });
        return Results.Ok(devices);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapPost("/api/iot/activate", async (ActivateIotRequest req, ILoggerFactory loggerFactory, IConfiguration config) =>
{
    var logger = loggerFactory.CreateLogger("IotSimulator");
    if (string.IsNullOrWhiteSpace(req.DeviceCode)) return Results.BadRequest(new { error = "DeviceCode is required." });

    try
    {
        var connStr = config.GetConnectionString("LocalConnection");
        using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var updateSql = @"UPDATE iot_devices SET ""IsOnline"" = true, status = 'ACTIVE', last_ping_time = CURRENT_TIMESTAMP WHERE device_code = @dc";
        var affected = await conn.ExecuteAsync(updateSql, new { dc = req.DeviceCode });
        if (affected == 0)
        {
            return Results.BadRequest(new { error = $"Thiết bị '{req.DeviceCode}' không tồn tại trong hệ thống (CSDL)!" });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error updating DB for IoT activate");
    }

    if (StandaloneDevices.TryGetValue(req.DeviceCode, out var existingState))
    {
        existingState.CancellationTokenSource?.Cancel();
    }

    var state = new StandaloneIotState
    {
        DeviceCode = req.DeviceCode,
        VehicleId = req.VehicleId,
        IsOnline = true,
        IsStreaming = false, // STREAM OFF mặc định!
        CurrentLat = req.Lat,
        CurrentLon = req.Lon,
        Temperature = req.TargetTemperature ?? -18.0,
        CancellationTokenSource = new CancellationTokenSource()
    };
    StandaloneDevices[req.DeviceCode] = state;

    _ = Task.Run(() => RunStandaloneIotSimulation(state, logger, config), state.CancellationTokenSource.Token);
    _ = PublishMqttStatusAsync(req.DeviceCode, true, logger);
    return Results.Ok(new { success = true, deviceCode = req.DeviceCode, isOnline = true, isStreaming = false });
});

app.MapPost("/api/iot/{deviceCode}/stream", async (string deviceCode, StreamIotRequest req, ILoggerFactory loggerFactory, IConfiguration config) =>
{
    var logger = loggerFactory.CreateLogger("IotSimulator");

    if (StandaloneDevices.TryGetValue(deviceCode, out var state))
    {
        state.IsStreaming = req.Stream;
        _ = SendMqttCommandAsync(deviceCode, req.Stream ? "START_STREAMING" : "STOP_STREAMING", logger);
        return Results.Ok(new { success = true, isStreaming = state.IsStreaming });
    }

    try
    {
        var connStr = config.GetConnectionString("LocalConnection");
        using var conn = new NpgsqlConnection(connStr);
        var isOnline = await conn.QuerySingleOrDefaultAsync<bool?>(
            @"SELECT ""IsOnline"" FROM iot_devices WHERE device_code = @dc",
            new { dc = deviceCode });

        if (isOnline != true)
        {
            return Results.BadRequest(new { error = $"Thiết bị '{deviceCode}' chưa được bật Online!" });
        }

        _ = SendMqttCommandAsync(deviceCode, req.Stream ? "START_STREAMING" : "STOP_STREAMING", logger);
        return Results.Ok(new
        {
            success = true,
            isStreaming = req.Stream,
            isHardwareDevice = true
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error checking DB for IoT stream toggle");
        return Results.BadRequest(new { error = "Lỗi khi kiểm tra trạng thái thiết bị IoT: " + ex.Message });
    }
});

app.MapPost("/api/iot/{deviceCode}/deactivate", async (string deviceCode, ILoggerFactory loggerFactory, IConfiguration config) =>
{
    var logger = loggerFactory.CreateLogger("IotSimulator");
    _ = PublishMqttStatusAsync(deviceCode, false, logger);
    if (StandaloneDevices.TryGetValue(deviceCode, out var state))
    {
        state.CancellationTokenSource?.Cancel();
        state.IsOnline = false;
        state.IsStreaming = false;
        StandaloneDevices.TryRemove(deviceCode, out _);
    }

    try
    {
        var connStr = config.GetConnectionString("LocalConnection");
        using var conn = new NpgsqlConnection(connStr);
        var updateSql = @"UPDATE iot_devices SET ""IsOnline"" = false, status = 'OFFLINE' WHERE device_code = @dc";
        await conn.ExecuteAsync(updateSql, new { dc = deviceCode });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error updating DB for IoT deactivate");
    }

    return Results.Ok(new { success = true });
});

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
        var backendUrl = config["BackendApiUrl"] ?? "http://localhost:5244";
        var res = await client.GetAsync($"{backendUrl}/api/dispatch/trip/{tripId}/route");
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
    
    string deviceId = req.DeviceId?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(deviceId))
    {
        return Results.BadRequest(new { error = "Vui lòng chỉ định Mã thiết bị IoT trong hệ thống cho chuyến đi này." });
    }

    try
    {
        var connStr = config.GetConnectionString("LocalConnection");
        using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        
        var checkExistsSql = "SELECT COUNT(1) FROM iot_devices WHERE device_code = @deviceId";
        using var checkCmd = new NpgsqlCommand(checkExistsSql, conn);
        checkCmd.Parameters.AddWithValue("deviceId", deviceId);
        if ((long)await checkCmd.ExecuteScalarAsync() == 0)
        {
            return Results.BadRequest(new { error = $"Thiết bị IoT '{deviceId}' không tồn tại trong hệ thống." });
        }

        var sql = "SELECT \"IsOnline\" FROM iot_devices WHERE device_code = @deviceId";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        var res = await cmd.ExecuteScalarAsync();
        
        if (res == null || !(bool)res)
        {
            if (req.AutoActivateIot)
            {
                logger.LogInformation($"Tự động bật Online (IsOnline=true, Stream ON) cho '{deviceId}' trước khi chạy chuyến.");
                var updateSql = @"UPDATE iot_devices SET ""IsOnline"" = true, status = 'ACTIVE', last_ping_time = CURRENT_TIMESTAMP WHERE device_code = @deviceId";
                using var updCmd = new NpgsqlCommand(updateSql, conn);
                updCmd.Parameters.AddWithValue("deviceId", deviceId);
                await updCmd.ExecuteNonQueryAsync();
                _ = PublishMqttStatusAsync(deviceId, true, logger);
                
                if (StandaloneDevices.TryGetValue(deviceId, out var std))
                {
                    std.IsOnline = true;
                    std.IsStreaming = true; // Khi bắt đầu lái xe, tự động stream on
                }
            }
            else
            {
                return Results.BadRequest(new { error = $"Thiết bị IoT '{deviceId}' hiện đang OFFLINE. Vui lòng bật IoT Online hoặc chọn Tự động Bật trước khi chạy!" });
            }
        }
        else if (StandaloneDevices.TryGetValue(deviceId, out var runningStd))
        {
            runningStd.IsStreaming = true; // Bật stream khi khởi hành
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error checking IoT status");
        return Results.BadRequest(new { error = "Lỗi khi kiểm tra trạng thái thiết bị IoT: " + ex.Message });
    }

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
        CurrentTemperature = req.TargetTemperature ?? -18.0,
        CancellationTokenSource = new CancellationTokenSource(),
        Path = DecodePolyline(req.Polyline)
    };
    
    if(state.Path.Count == 0) return Results.BadRequest("Invalid polyline.");
    
    state.CurrentLat = state.Path[0].Lat;
    state.CurrentLon = state.Path[0].Lon;
    
    FleetState[deviceId] = state;
    
    _ = Task.Run(() => RunVehicleSimulation(state, logger, config), state.CancellationTokenSource.Token);
    
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

app.MapPost("/api/fleet/{deviceId}/pause", (string deviceId) =>
{
    if (FleetState.TryGetValue(deviceId, out var state))
    {
        state.IsPaused = !state.IsPaused;
        return Results.Ok(new { isPaused = state.IsPaused });
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

async Task RunVehicleSimulation(VehicleSimulationState state, ILogger logger, IConfiguration config)
{
    string clientId = $"VEHICLE_SIM_{state.DeviceId}_{Guid.NewGuid().ToString().Substring(0, 8)}";
    string statusTopic = $"telemetry/coldchain/{state.DeviceId}/status";
    string offlinePayload = JsonSerializer.Serialize(new { status = "OFFLINE", clientId = clientId, timestamp = DateTime.UtcNow.ToString("O") });

    var optionsBuilder = new MqttClientOptionsBuilder()
        .WithTcpServer("8.231.129.222", 1883)
        .WithCredentials("esp32user", "183732")
        .WithClientId(clientId);

    if (!state.IsHybridMode)
    {
        optionsBuilder
            .WithWillTopic(statusTopic)
            .WithWillPayload(System.Text.Encoding.UTF8.GetBytes(offlinePayload))
            .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
    }

    var mqttClient = factory.CreateMqttClient();
    var options = optionsBuilder.Build();

    try
    {
        await mqttClient.ConnectAsync(options, state.CancellationTokenSource.Token);
        logger.LogInformation($"[{state.DeviceId}] Connected to MQTT as {clientId}. HybridMode={state.IsHybridMode}");
        
        if (!state.IsHybridMode)
        {
            string onlinePayload = JsonSerializer.Serialize(new { status = "ONLINE", clientId = clientId, timestamp = DateTime.UtcNow.ToString("O") });
            var onlineMsg = new MqttApplicationMessageBuilder()
                .WithTopic(statusTopic)
                .WithPayload(onlinePayload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await mqttClient.PublishAsync(onlineMsg, state.CancellationTokenSource.Token);
            logger.LogInformation($"[{state.DeviceId}] Published MQTT Status ONLINE to backend worker.");
        }

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
                        
                        if (root.TryGetProperty("IsSimulated", out var simEl) && simEl.GetBoolean() == true)
                        {
                            return; // Bỏ qua message do chính Simulator vừa gửi
                        }

                        if (state.InjectTemp) {
                            state.CurrentTemperature = state.TargetTemperature;
                        } else {
                            if (root.TryGetProperty("TempC", out var tempEl)) state.CurrentTemperature = tempEl.GetDouble();
                        }
                        
                        
                        if (state.UseRealGps) {
                            if (root.TryGetProperty("Lat", out var latEl)) state.CurrentLat = latEl.GetDouble();
                            if (root.TryGetProperty("Lon", out var lonEl)) state.CurrentLon = lonEl.GetDouble();
                        } else {
                            if (!state.IsPaused)
                            {
                                InterpolatePosition(state, (DateTime.UtcNow - lastMessageTime).TotalSeconds * (state.SpeedKmh / 3600.0));
                            }
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
                
                try
                {
                    var connStr = config.GetConnectionString("LocalConnection");
                    using var conn = new NpgsqlConnection(connStr);
                    await conn.ExecuteAsync(@"UPDATE iot_devices SET ""IsOnline"" = true, last_ping_time = CURRENT_TIMESTAMP WHERE device_code = @dc", new { dc = state.DeviceId });
                }
                catch { }
                
                if (!state.IsPaused)
                {
                    double distanceKm = (state.SpeedKmh / 3600.0) * (tickDelayMs / 1000.0);
                    InterpolatePosition(state, distanceKm);
                }
                
                await Task.Delay(tickDelayMs, state.CancellationTokenSource.Token);
            }
        }
        
        state.IsRunning = false;
        if (mqttClient.IsConnected)
        {
            try
            {
                string offPayload = JsonSerializer.Serialize(new { status = "OFFLINE", clientId = $"ESP32_SIM_{state.DeviceId}", timestamp = DateTime.UtcNow.ToString("O") });
                var offMsg = new MqttApplicationMessageBuilder()
                    .WithTopic(statusTopic)
                    .WithPayload(offPayload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();
                await mqttClient.PublishAsync(offMsg);
                await mqttClient.DisconnectAsync();
                logger.LogInformation($"[{state.DeviceId}] Published MQTT Status OFFLINE upon stopping.");
            }
            catch { }
        }
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
        try
        {
            if (mqttClient.IsConnected)
            {
                if (!state.IsHybridMode)
                {
                    string offPayload = JsonSerializer.Serialize(new { status = "OFFLINE", clientId = $"ESP32_SIM_{state.DeviceId}", timestamp = DateTime.UtcNow.ToString("O") });
                    var offMsg = new MqttApplicationMessageBuilder()
                        .WithTopic($"telemetry/coldchain/{state.DeviceId}/status")
                        .WithPayload(offPayload)
                        .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build();
                    await mqttClient.PublishAsync(offMsg);
                }
                await mqttClient.DisconnectAsync();
            }
        }
        catch { }
    }
}

static List<Coordinate> DecodePolyline(string encodedPoints)
{
    if (string.IsNullOrEmpty(encodedPoints))
        return new List<Coordinate>();

    var polylineChars = encodedPoints.ToCharArray();
    int index = 0, currentLat = 0, currentLng = 0;
    var coordinates = new List<Coordinate>();

    while (index < polylineChars.Length)
    {
        int sum = 0, shifter = 0, next5Bits = 0;
        do
        {
            if (index >= polylineChars.Length) break;
            next5Bits = polylineChars[index++] - 63;
            sum |= (next5Bits & 31) << shifter;
            shifter += 5;
        } while (next5Bits >= 32 && index < polylineChars.Length);
        if (index > polylineChars.Length || (index == polylineChars.Length && next5Bits >= 32)) break;
        currentLat += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

        sum = 0;
        shifter = 0;
        do
        {
            if (index >= polylineChars.Length) break;
            next5Bits = polylineChars[index++] - 63;
            sum |= (next5Bits & 31) << shifter;
            shifter += 5;
        } while (next5Bits >= 32 && index < polylineChars.Length);
        if (index > polylineChars.Length || (index == polylineChars.Length && next5Bits >= 32)) break;
        currentLng += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

        coordinates.Add(new Coordinate(currentLat / 1E5, currentLng / 1E5));
    }
    return coordinates;
}

async Task PublishMqttStatusAsync(string deviceCode, bool isOnline, ILogger logger)
{
    var statusClient = factory.CreateMqttClient();
    var options = new MqttClientOptionsBuilder()
        .WithTcpServer("8.231.129.222", 1883)
        .WithCredentials("esp32user", "183732")
        .WithClientId($"STATUS_PUB_{deviceCode}_{Guid.NewGuid():N}")
        .Build();
        
    try
    {
        await statusClient.ConnectAsync(options);
        string statusStr = isOnline ? "ONLINE" : "OFFLINE";
        string clientId = $"ESP32_SIM_{deviceCode}";
        string payload = JsonSerializer.Serialize(new { status = statusStr, clientId = clientId, timestamp = DateTime.UtcNow.ToString("O") });
        string topic = $"telemetry/coldchain/{deviceCode}/status";

        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await statusClient.PublishAsync(msg);
        await statusClient.DisconnectAsync();
        logger.LogInformation($"[{deviceCode}] Published MQTT Status: {statusStr} to topic {topic}");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, $"[{deviceCode}] Failed to publish MQTT status ({isOnline})");
    }
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

async Task RunStandaloneIotSimulation(StandaloneIotState state, ILogger logger, IConfiguration config)
{
    string clientId = $"ESP32_SIM_{state.DeviceCode}";
    string statusTopic = $"telemetry/coldchain/{state.DeviceCode}/status";
    string offlinePayload = JsonSerializer.Serialize(new { status = "OFFLINE", clientId = clientId, timestamp = DateTime.UtcNow.ToString("O") });

    var mqttClient = factory.CreateMqttClient();
    var options = new MqttClientOptionsBuilder()
        .WithTcpServer("8.231.129.222", 1883)
        .WithCredentials("esp32user", "183732")
        .WithClientId(clientId)
        .WithWillTopic(statusTopic)
        .WithWillPayload(System.Text.Encoding.UTF8.GetBytes(offlinePayload))
        .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
        .Build();

    try
    {
        await mqttClient.ConnectAsync(options, state.CancellationTokenSource.Token);
        logger.LogInformation($"[{state.DeviceCode}] Standalone IoT connected to MQTT. IsOnline=true, Stream OFF (Waiting for API iot-check command)...");
        
        string onlinePayload = JsonSerializer.Serialize(new { status = "ONLINE", clientId = clientId, timestamp = DateTime.UtcNow.ToString("O") });
        var onlineMsg = new MqttApplicationMessageBuilder()
            .WithTopic(statusTopic)
            .WithPayload(onlinePayload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await mqttClient.PublishAsync(onlineMsg, state.CancellationTokenSource.Token);
        logger.LogInformation($"[{state.DeviceCode}] Published MQTT Status ONLINE to backend worker.");

        string commandTopic = $"command/coldchain/{state.DeviceCode}";
        mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            try
            {
                var topic = e.ApplicationMessage.Topic;
                var payloadStr = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                logger.LogInformation($"[{state.DeviceCode}] Nhận lệnh từ Backend MQTT topic {topic}: {payloadStr}");
                
                if (payloadStr.Contains("START_STREAMING", StringComparison.OrdinalIgnoreCase))
                {
                    state.IsStreaming = true;
                    logger.LogInformation($"[{state.DeviceCode}] => STREAM ON! (Đã kích hoạt gửi tín hiệu telemetry!)");
                }
                else if (payloadStr.Contains("STOP_STREAMING", StringComparison.OrdinalIgnoreCase))
                {
                    state.IsStreaming = false;
                    logger.LogInformation($"[{state.DeviceCode}] => STREAM OFF! (Đã tạm dừng phát telemetry)");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"[{state.DeviceCode}] Lỗi xử lý MQTT Command: {ex.Message}");
            }
            return Task.CompletedTask;
        };

        await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(commandTopic).Build());
        logger.LogInformation($"[{state.DeviceCode}] Subscribed topic: {commandTopic}");

        var rnd = new Random();
        var connStr = config.GetConnectionString("LocalConnection");

        while (!state.CancellationTokenSource.Token.IsCancellationRequested && state.IsOnline)
        {
            try
            {
                using var conn = new NpgsqlConnection(connStr);
                await conn.ExecuteAsync(@"UPDATE iot_devices SET ""IsOnline"" = true, last_ping_time = CURRENT_TIMESTAMP WHERE device_code = @dc", new { dc = state.DeviceCode });
            }
            catch { }

            if (state.IsStreaming)
            {
                var runningTrip = FleetState.Values.FirstOrDefault(f => f.DeviceId == state.DeviceCode && f.IsRunning);

                double sendLat = state.CurrentLat;
                double sendLon = state.CurrentLon;
                if (runningTrip != null)
                {
                    sendLat = runningTrip.CurrentLat;
                    sendLon = runningTrip.CurrentLon;
                    state.CurrentLat = sendLat;
                    state.CurrentLon = sendLon;
                }

                bool isForHybridTrip = (runningTrip != null && runningTrip.IsHybridMode);

                if (runningTrip == null || isForHybridTrip)
                {
                    double temp = state.Temperature + (rnd.NextDouble() * 0.6 - 0.3);
                    double currentTemp = Math.Round(temp, 1);

                    string topic = isForHybridTrip 
                        ? $"telemetry/coldchain/{state.DeviceCode}/raw" 
                        : $"telemetry/coldchain/{state.DeviceCode}/data";

                    var payload = new
                    {
                        DeviceId = state.DeviceCode,
                        TempC = currentTemp,
                        DoorOpen = state.IsDoorOpen,
                        Lat = sendLat,
                        Lon = sendLon,
                        Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                        IsSimulated = !isForHybridTrip
                    };

                    string json = JsonSerializer.Serialize(payload);

                    var msg = new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(json)
                        .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build();

                    await mqttClient.PublishAsync(msg, state.CancellationTokenSource.Token);
                    
                    if (isForHybridTrip)
                        logger.LogInformation($"[{state.DeviceCode} STREAM ON -> HYBRID RAW] Pinged Raw Temp:{currentTemp}°C to vehicle trip simulator.");
                    else
                        logger.LogInformation($"[{state.DeviceCode} STREAM ON] Pinged MQTT Telemetry Temp:{currentTemp}°C Lat:{sendLat} Lon:{sendLon}");
                }
            }

            await Task.Delay(8000, state.CancellationTokenSource.Token);
        }
    }
    catch (TaskCanceledException)
    {
        logger.LogInformation($"[{state.DeviceCode}] Standalone simulation stopped.");
    }
    catch (Exception ex)
    {
        logger.LogError($"[{state.DeviceCode}] Error in standalone simulation: {ex.Message}");
    }
    finally
    {
        state.IsOnline = false;
        state.IsStreaming = false;
        try
        {
            if (mqttClient.IsConnected)
            {
                string offPayload = JsonSerializer.Serialize(new { status = "OFFLINE", clientId = $"ESP32_SIM_{state.DeviceCode}", timestamp = DateTime.UtcNow.ToString("O") });
                var offMsg = new MqttApplicationMessageBuilder()
                    .WithTopic(statusTopic)
                    .WithPayload(offPayload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();
                await mqttClient.PublishAsync(offMsg);
                logger.LogInformation($"[{state.DeviceCode}] Published MQTT Status OFFLINE upon disconnect.");
                await mqttClient.DisconnectAsync();
            }
        }
        catch { }
    }
}

public class SimulationRequest
{
    public string? Polyline { get; set; }
    public string? DeviceId { get; set; }
    public double SpeedKmh { get; set; } = 60;
    public double? TargetTemperature { get; set; }
    public bool IsHybridMode { get; set; }
    public bool InjectTemp { get; set; }
    public bool AutoActivateIot { get; set; } = true;
}

public class ActivateIotRequest
{
    public string? DeviceCode { get; set; }
    public Guid? VehicleId { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double? TargetTemperature { get; set; }
}

public class StreamIotRequest
{
    public bool Stream { get; set; }
}

public class StandaloneIotState
{
    public string DeviceCode { get; set; } = "";
    public Guid? VehicleId { get; set; }
    public bool IsOnline { get; set; }
    public bool IsStreaming { get; set; }
    public double CurrentLat { get; set; }
    public double CurrentLon { get; set; }
    public double Temperature { get; set; }
    public bool IsDoorOpen { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationTokenSource? CancellationTokenSource { get; set; }
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
    public bool IsPaused { get; set; }
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


