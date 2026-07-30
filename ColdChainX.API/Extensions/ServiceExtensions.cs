using System.Text;
using System.Text.Json.Serialization;
using AutoMapper;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Mappings;
using ColdChainX.Application.Services;
using ColdChainX.API.Services;
using ColdChainX.API.Workers;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Repositories;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Infrastructure.Services.Firebase;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Responses;
using ColdChainX.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace ColdChainX.API.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddProjectServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
            services.Configure<GoogleAuthSettings>(
                configuration.GetSection(GoogleAuthSettings.SectionName));

            // Required for IHttpContextAccessor used in SimplePdfService to build absolute PDF URLs
            services.AddHttpContextAccessor();

            // CORS
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            var pgHost = Environment.GetEnvironmentVariable("PGHOST");
            var pgUser = Environment.GetEnvironmentVariable("PGUSER");
            var pgPort = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
            var pgDatabase = Environment.GetEnvironmentVariable("PGDATABASE");
            var pgPassword = Environment.GetEnvironmentVariable("PGPASSWORD");

            string connectionString;
            if (!string.IsNullOrEmpty(pgHost) && !string.IsNullOrEmpty(pgUser) && !string.IsNullOrEmpty(pgPassword))
            {
                connectionString = $"Host={pgHost};Port={pgPort};Database={pgDatabase ?? "postgres"};Username={pgUser};Password={pgPassword};Include Error Detail=true";
            }
            else
            {
                connectionString = configuration.GetConnectionString("LocalConnection")
                    ?? throw new InvalidOperationException("ConnectionStrings:LocalConnection was not found.");
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(connectionString, b => b.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null));
                options.ConfigureWarnings(warnings => 
                    warnings.Ignore(new Microsoft.Extensions.Logging.EventId(10622)));
            });

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IWarehouseRepository, WarehouseRepository>();

            services.AddScoped<IWarehouseReceiptRepository, WarehouseReceiptRepository>();
            services.AddScoped<IVehicleRepository, VehicleRepository>();
            services.AddScoped<IDriverRepository, DriverRepository>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IGoogleAuthService, GoogleAuthService>();
            services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
            services.AddScoped<IGoogleOAuthClient, GoogleOAuthClient>();
            services.AddScoped<IVehicleService, VehicleService>();
            services.AddScoped<IDriverService, DriverService>();
            services.AddScoped<IJwtService, JwtService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IPermissionService, PermissionService>();
            services.AddScoped<IWorkAssignmentService, WorkAssignmentService>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            services.AddScoped<IWarehouseService, WarehouseService>();

            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<IRealtimeTelemetryService, ColdChainX.API.Implementations.RedisRealtimeTelemetryService>();
            services.AddHttpClient<ILocationService, GoongLocationService>(client =>
            {
                client.BaseAddress = new Uri("https://rsapi.goong.io/");
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.Add("User-Agent", "ColdChainX/1.0");
            });
            services.AddHttpClient<IGoongMapService, GoongMapService>(client =>
            {
                client.BaseAddress = new Uri("https://rsapi.goong.io/");
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.Add("User-Agent", "ColdChainX/1.0");
            });
            services.AddHttpClient(GoogleOAuthClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://oauth2.googleapis.com/");
                client.Timeout = TimeSpan.FromSeconds(20);
            });
            services.AddScoped<IFileService, FileService>();
            services.AddScoped<ComplianceRulesEngine>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IQuotationService, QuotationService>();
            services.AddScoped<IRouteService, RouteService>();
            services.AddScoped<IAsnService, AsnService>();
            services.AddScoped<IPdfService, SimplePdfService>();
            services.AddScoped<IWeightTierService, WeightTierService>();
            services.AddScoped<ISystemConfigService, SystemConfigService>();
            services.AddScoped<IContractService, ContractService>();
            services.AddScoped<IContractAppendixService, ContractAppendixService>();
            services.AddScoped<IChatService, ChatService>();
            services.AddFirebaseCloudMessaging(configuration);
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IWarehouseReceiptService, WarehouseReceiptService>();
            services.AddScoped<IInvoiceService, InvoiceService>();
            services.AddScoped<IInventoryAnalysisService, InventoryAnalysisService>();
            services.AddScoped<IIncidentReportService, IncidentReportService>();
            services.AddScoped<IIncidentRescueService, IncidentRescueService>();
            services.AddScoped<IIncidentRealtimeNotifier, IncidentRealtimeNotifier>();
            services.AddScoped<IClaimService, ClaimService>();
            services.AddScoped<IDeliveryEventService, DeliveryEventService>();
            services.AddScoped<IPaymentGatewayService, PayOsPaymentService>();
            services.AddScoped<IErpIntegrationService, MockErpIntegrationService>();

            services.AddScoped<IOutboundOrderService, OutboundOrderService>();
            services.AddScoped<IFleetManagementService, FleetManagementService>();
            services.AddScoped<IPdfGeneratorService, PdfGeneratorService>();
            services.AddScoped<IWarehouseFlowService, WarehouseFlowService>();
            services.AddScoped<IColdChainMonitoringService, ColdChainMonitoringService>();
            services.AddSingleton<IColdChainRiskService, ColdChainRiskService>();
            services.AddSingleton<IAiAlertingControlService, AiAlertingControlService>();
            services.AddScoped<IMqttCommandPublisher, MqttCommandPublisher>();
            if (configuration.GetValue("HostedWorkers:FleetCompliance", true))
            {
                services.AddHostedService<FleetComplianceWorker>();
            }
            
            // Dispatch and Load Planning
            services.AddHttpClient<ColdChainX.Infrastructure.Integration.GeminiLoadOptimizerClient>();
            services.AddScoped<ICargoCompatibilityService, CargoCompatibilityService>();
            services.AddScoped<IDispatchService, DispatchService>();
            services.AddScoped<IDriverAvailabilityService, DriverAvailabilityService>();
            services.AddScoped<IServiceCatalogService, ServiceCatalogService>();

            services.AddSignalR();

            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ColdChainX.Application.Features.Inbound.Commands.ProcessInboundQcCommand).Assembly));
            services.AddAutoMapper(typeof(MappingProfile));

            services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
            services.AddSingleton<IGoogleLoginCodeStore, GoogleLoginCodeStore>();

            services.AddValidatorsFromAssemblyContaining<Application.Validators.RegisterRequestValidator>();
            services.AddFluentValidationAutoValidation();
            services.AddFluentValidationClientsideAdapters();
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });

            // Removed duplicate validator registration line

            // JWT Authentication
            var jwt = configuration.GetSection("JwtSettings").Get<JwtSettings>()
                      ?? throw new InvalidOperationException("JwtSettings is missing.");
            var key = Encoding.UTF8.GetBytes(jwt.Key);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken)
                            && (path.StartsWithSegments(new PathString("/hubs/notifications"))
                                || path.StartsWithSegments(new PathString("/hubs/chat"))
                                || path.StartsWithSegments(new PathString("/hubs/monitoring"))))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        if (context.Response.HasStarted)
                            return;

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(
                            ApiResponse<object>.Failure(
                                "Authentication is required or the access token is invalid.",
                                StatusCodes.Status401Unauthorized));
                    },
                    OnForbidden = async context =>
                    {
                        if (context.Response.HasStarted)
                            return;

                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(
                            ApiResponse<object>.Failure(
                                "You do not have permission to perform this action.",
                                StatusCodes.Status403Forbidden));
                    }
                };
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("WarehouseWorkerOnly", policy => policy.RequireRole("WarehouseWorker"));
                options.AddPolicy("DriverOnly", policy => policy.RequireRole("Driver"));
                options.AddPolicy("CustomerOnly", policy => policy.RequireRole("Customer"));
            });

            return services;
        }
    }
}
