using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
builder.Services.AddHostedService<TelemetryMqttWorker>();
builder.Services.AddHostedService<TelemetryProcessorWorker>();
builder.Services.AddHostedService<InventoryAgingWorker>();

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

await app.Services.ApplyAuthSchemaCompatibilityPatchAsync(app.Logger);

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
// Enable body buffering for PayOS webhook HMAC verification
// (applies only to the webhook endpoint to avoid memory overhead on other endpoints)
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
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
