using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Any;
using ColdChainX.API.Extensions;
using ColdChainX.API.Middleware;
using ColdChainX.API.Models;
using ColdChainX.API.Services;
using ColdChainX.API.Swagger;
using ColdChainX.API.Workers;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using System.Threading.Channels;
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
DotEnvLoader.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));
DotEnvLoader.Load(Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".env")));

var configuration = builder.Configuration;

builder.Services.AddProjectServices(configuration);
builder.Services.AddSingleton(Channel.CreateUnbounded<TelemetryData>(new UnboundedChannelOptions
{
    SingleReader = false,
    SingleWriter = false
}));
builder.Services.AddSingleton<RedisService>();

if (configuration.GetValue("HostedWorkers:TelemetryMqtt", true))
{
    builder.Services.AddHostedService<TelemetryMqttWorker>();
}

if (configuration.GetValue("HostedWorkers:TelemetryProcessor", true))
{
    builder.Services.AddHostedService<TelemetryProcessorWorker>();
}

if (configuration.GetValue("HostedWorkers:IotWatchdog", true))
{
    builder.Services.AddHostedService<IotWatchdogWorker>();
}

if (configuration.GetValue("HostedWorkers:InventoryAging", true))
{
    builder.Services.AddHostedService<InventoryAgingWorker>();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ColdChainX API", Version = "v1" });

    var xmlFiles = new[] { "ColdChainX.API.xml", "ColdChainX.Application.xml", "ColdChainX.Shared.xml" };
    foreach (var file in xmlFiles)
    {
        var xmlPath = Path.Combine(AppContext.BaseDirectory, file);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }
    }

    c.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace("+", "."));
    c.SchemaFilter<CreateOrderRequestSchemaFilter>();
    c.SchemaFilter<EnumSchemaFilter>();
    c.OperationFilter<CreateOrderFormOperationFilter>();
    c.OperationFilter<RegisterOperationFilter>();
    c.OperationFilter<CreateCustomerOperationFilter>();
    c.OperationFilter<CreateDriverOperationFilter>();
    c.OperationFilter<RemoveAuthFromCreateAccountsFilter>();
    c.OperationFilter<DispatchOperationFilter>();
    c.OperationFilter<WarehouseReceiptOperationFilter>();
    c.UseInlineDefinitionsForEnums();
    c.OperationFilter<UpdateContractDraftOperationFilter>();

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme.",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

if (configuration.GetValue("Startup:ApplyDatabaseBootstrap", true))
{
    await app.Services.ApplyAuthSchemaCompatibilityPatchAsync(app.Logger);
}
else
{
    app.Logger.LogInformation("Database bootstrap skipped by Startup:ApplyDatabaseBootstrap=false.");
}

app.UseMiddleware<ExceptionMiddleware>();

// Cần thiết khi deploy trên Render/cloud (reverse proxy).
// Đọc X-Forwarded-Proto & X-Forwarded-Host để Request.Scheme và Request.Host
// phản ánh đúng URL công khai thay vì địa chỉ nội bộ của proxy.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
                     | ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

// Enable Swagger in all environments for testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ColdChainX API v1");
    c.RoutePrefix = "swagger"; // Access at /swagger
});

app.UseRouting();
app.UseCors("CorsPolicy");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapGet("/", () => Results.Ok(new
{
    status = "healthy",
    service = "ColdChainX API",
    version = "1.0.0",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName
})).WithName("HealthCheck");

app.MapControllers();

var monitoringApi = app.MapGroup("/api/minimal/monitoring")
    .RequireAuthorization()
    .WithTags("Cold Chain Monitoring Minimal");

monitoringApi.MapGet("/tracking/{tripId:guid}", async (
    Guid tripId,
    int maxPoints,
    ApplicationDbContext db,
    RedisService redisService,
    IColdChainRiskService riskService,
    CancellationToken cancellationToken) =>
{
    maxPoints = maxPoints <= 0 ? 120 : Math.Clamp(maxPoints, 20, 1000);

    var trip = await db.MasterTrips
        .Include(t => t.OriginLocation)
        .Include(t => t.DestinationLocation)
        .Include(t => t.Vehicle)
            .ThenInclude(v => v!.IotDevices)
        .FirstOrDefaultAsync(t => t.TripId == tripId, cancellationToken);

    if (trip == null)
    {
        return Results.NotFound(new { Success = false, Error = "Trip not found." });
    }

    var rawLogs = await db.TelemetryLogs
        .Where(t => t.TripId == tripId)
        .OrderByDescending(t => t.Timestamp)
        .Take(2000)
        .OrderBy(t => t.Timestamp)
        .Select(t => new
        {
            t.Timestamp,
            t.Temperature,
            t.Latitude,
            t.Longitude
        })
        .ToListAsync(cancellationToken);

    var points = rawLogs
        .Select(t => new TrackingPoint(t.Timestamp, t.Temperature, t.Latitude, t.Longitude))
        .ToList();
    var sampled = TrackingDownsampler.Downsample(points, maxPoints);

    var device = trip.Vehicle?.IotDevices.FirstOrDefault();
    var redisKey = string.IsNullOrWhiteSpace(device?.DeviceCode)
        ? device?.DeviceId.ToString()
        : device.DeviceCode;
    var latest = redisKey == null ? null : await redisService.GetLatestAsync(redisKey);
    var forecastInput = points.Select(p => (double)p.TempC).ToList();
    if (latest != null)
    {
        forecastInput.Add(latest.TempC);
    }

    var forecast = riskService.ForecastTemperature(forecastInput, horizon: 30);

    return Results.Ok(new
    {
        Success = true,
        Data = new
        {
            trip.TripId,
            trip.Status,
            Device = device == null ? null : new
            {
                device.DeviceId,
                device.DeviceCode,
                device.Status,
                device.LastPingTime
            },
            LatestTelemetry = latest,
            Forecast = forecast,
            RawPointCount = points.Count,
            SampledPointCount = sampled.Count,
            Points = sampled.Select(p => new
            {
                p.Timestamp,
                p.TempC,
                p.Lat,
                p.Lon
            })
        }
    });
});

monitoringApi.MapPost("/ml/ssa/train", async (
    bool overwrite,
    IColdChainRiskService riskService,
    CancellationToken cancellationToken) =>
{
    var result = await riskService.TrainSsaModelAsync(overwrite, cancellationToken);
    return result.Success
        ? Results.Ok(new
        {
            result.Success,
            result.Message,
            result.DataPath,
            result.ModelPath,
            result.WasTrained,
            result.RowCount,
            result.WindowSize,
            result.SeriesLength,
            result.Horizon
        })
        : Results.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError);
});

app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<MonitoringHub>("/hubs/monitoring");

app.Run();
