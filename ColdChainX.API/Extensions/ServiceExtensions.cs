using System.Text;
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
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Repositories;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Shared.Constants;
using Npgsql;

namespace ColdChainX.API.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddProjectServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

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
                options.UseNpgsql(connectionString, b => b.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null)));

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IWarehouseRepository, WarehouseRepository>();
            services.AddScoped<IWarehouseZoneRepository, WarehouseZoneRepository>();
            services.AddScoped<IWarehouseLocationRepository, WarehouseLocationRepository>();
            services.AddScoped<IWarehouseReceiptRepository, WarehouseReceiptRepository>();
            services.AddScoped<IVehicleRepository, VehicleRepository>();
            services.AddScoped<IDriverRepository, DriverRepository>();
            services.AddScoped<IWarehouseAttachmentRepository, WarehouseAttachmentRepository>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IVehicleService, VehicleService>();
            services.AddScoped<IDriverService, DriverService>();
            services.AddScoped<IJwtService, JwtService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IWarehouseService, WarehouseService>();
            services.AddScoped<IWarehouseZoneService, WarehouseZoneService>();
            services.AddScoped<IWarehouseLocationService, WarehouseLocationService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddHttpClient<ILocationService, GoongLocationService>(client =>
            {
                client.BaseAddress = new Uri("https://rsapi.goong.io/");
                client.Timeout = TimeSpan.FromSeconds(20);
            });
            services.AddScoped<IFileService, FileService>();
            services.AddScoped<IAttachmentManagementService, AttachmentManagementService>();
            services.AddScoped<ComplianceRulesEngine>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IQuotationService, QuotationService>();
            services.AddScoped<IPdfService, SimplePdfService>();
            services.AddScoped<IContractService, ContractService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IWarehouseReceiptService, WarehouseReceiptService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IOutboundOrderService, OutboundOrderService>();
            services.AddScoped<IInventoryHoldRepository, InventoryHoldRepository>();
            services.AddScoped<IInventoryHoldService, InventoryHoldService>();
            services.AddScoped<ICycleCountRepository, CycleCountRepository>();
            services.AddScoped<ICycleCountService, CycleCountService>();
            
            // Dispatch and Load Planning
            services.AddHttpClient<ColdChainX.Infrastructure.Integration.GeminiLoadOptimizerClient>();
            services.AddScoped<IDispatchService, DispatchService>();

            services.AddSignalR();

            services.AddAutoMapper(typeof(MappingProfile));

            services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

            services.AddValidatorsFromAssemblyContaining<Application.Validators.RegisterRequestValidator>();
            services.AddFluentValidationAutoValidation();
            services.AddFluentValidationClientsideAdapters();
            services.AddControllers();

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
                            && path.StartsWithSegments(new PathString("/hubs/notifications")))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("ManagerOnly", policy => policy.RequireRole("Manager"));
                options.AddPolicy("DriverOnly", policy => policy.RequireRole("Driver"));
                options.AddPolicy("CustomerOnly", policy => policy.RequireRole("Customer"));
            });

            return services;
        }
    }
}
