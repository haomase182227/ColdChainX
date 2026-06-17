using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.API.Controllers;

namespace ColdChainX.API.Swagger
{
    public class SuggestLoadRequestSchemaFilter : ISchemaFilter
    {
        private readonly IServiceProvider _serviceProvider;

        public SuggestLoadRequestSchemaFilter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type != typeof(SuggestLoadRequest))
                return;

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    // 1. Vehicles: Status = ACTIVE
                    var vehicles = db.Vehicles
                        .Where(v => v.Status == "ACTIVE")
                        .Select(v => $"{v.VehicleId}: {v.TruckPlate} — {v.VehicleType} | {v.MaxWeight}kg / {v.MaxCbm}m³")
                        .ToList();

                    // 2. Orders: Status = IN_WAREHOUSE
                    var orders = db.TransportOrders
                        .Where(o => o.Status == "IN_WAREHOUSE")
                        .OrderByDescending(o => o.CreatedAt)
                        .Select(o => $"{o.OrderId}: {o.TrackingCode} — {o.ItemName} | {o.ExpectedWeightKg}kg / {o.ExpectedCbm}m³ ({o.TempCondition})")
                        .ToList();

                    // Apply to VehicleId
                    ApplyEnum(schema, "VehicleId", vehicles);
                    ApplyEnum(schema, "vehicleId", vehicles);

                    // Apply to OrderIds (array)
                    ApplyArrayEnum(schema, "OrderIds", orders);
                    ApplyArrayEnum(schema, "orderIds", orders);
                }
            }
            catch (Exception)
            {
                // Silence DB errors
            }
        }

        private static void ApplyEnum(OpenApiSchema schema, string propertyName, IEnumerable<string> values)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null)
                return;

            property.Type = "string";
            property.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }

        private static void ApplyArrayEnum(OpenApiSchema schema, string propertyName, IEnumerable<string> values)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null)
                return;

            if (property.Type == "array" && property.Items != null)
            {
                property.Items.Type = "string";
                property.Items.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
            }
        }

        private static OpenApiSchema? FindProperty(OpenApiSchema schema, string propertyName)
        {
            if (schema.Properties.TryGetValue(propertyName, out var exactMatch))
                return exactMatch;

            return schema.Properties
                .FirstOrDefault(entry => string.Equals(entry.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                .Value;
        }
    }
}
