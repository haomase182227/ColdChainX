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
using ColdChainX.Infrastructure.Services.Firebase;
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

if (configuration.GetValue("HostedWorkers:ScheduleBookingStatus", true))
{
    builder.Services.AddHostedService<ScheduleBookingStatusWorker>();
}

if (configuration.GetValue("HostedWorkers:AutoInvoicing", true))
{
    builder.Services.AddHostedService<AutoInvoicingWorker>();
}

if (configuration.GetValue("HostedWorkers:IncidentSlaEscalation", true))
{
    builder.Services.AddHostedService<IncidentSlaEscalationWorker>();
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
    c.OperationFilter<CommonApiResponsesOperationFilter>();
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

var firebaseStatus = app.Services.GetRequiredService<FirebaseConfigurationStatus>();
if (firebaseStatus.IsConfigured)
{
    app.Logger.LogInformation(
        "Firebase Cloud Messaging initialized from {CredentialSource}.",
        firebaseStatus.CredentialSource);
}
else
{
    app.Logger.LogWarning(
        "Firebase Cloud Messaging is unavailable: {FirebaseConfigurationError}",
        firebaseStatus.Error);
}

if (configuration.GetValue("Startup:ApplyDatabaseBootstrap", true))
{
    await app.Services.ApplyAuthSchemaCompatibilityPatchAsync(app.Logger);
}
else
{
    app.Logger.LogInformation("Database bootstrap skipped by Startup:ApplyDatabaseBootstrap=false.");
}

app.UseMiddleware<ExceptionMiddleware>();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
                     | ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ColdChainX API v1");
    c.RoutePrefix = "swagger"; // Access at /swagger
});

app.UseRouting();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/payments/bank-webhook"))
        context.Request.EnableBuffering();
    await next();
});

app.UseCors("CorsPolicy");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    status = "healthy",
    service = "ColdChainX API",
    version = "1.0.0",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName
})).WithName("HealthCheck");

app.MapControllers();



app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<MonitoringHub>("/hubs/monitoring");

app.Run();
