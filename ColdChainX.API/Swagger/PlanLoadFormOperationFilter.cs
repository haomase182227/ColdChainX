using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using ColdChainX.Infrastructure.Persistence;

namespace ColdChainX.API.Swagger
{
    public class PlanLoadFormOperationFilter : IOperationFilter
    {
        private readonly IServiceProvider _serviceProvider;

        public PlanLoadFormOperationFilter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            var httpMethod = context.ApiDescription.HttpMethod;

            // Target only the plan-load endpoint (POST /api/Dispatch/plan-load or similar casing)
            if (!string.Equals(path, "api/Dispatch/plan-load", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, "api/dispatch/plan-load", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (operation.RequestBody?.Content == null
                || !operation.RequestBody.Content.TryGetValue("multipart/form-data", out var mediaType)
                || mediaType.Schema == null)
            {
                return;
            }

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

                    // 2. Locations: Status = ACTIVE
                    var locations = db.Locations
                        .Where(l => l.Status == "ACTIVE")
                        .OrderBy(l => l.Address)
                        .Select(l => $"{l.LocationId}: {l.Address}")
                        .ToList();

                    // 3. Orders: Status = IN_WAREHOUSE
                    var orders = db.TransportOrders
                        .Where(o => o.Status == "IN_WAREHOUSE")
                        .OrderByDescending(o => o.CreatedAt)
                        .Select(o => $"{o.OrderId}: {o.TrackingCode} — {o.ItemName} | {o.ExpectedWeightKg}kg / {o.ExpectedCbm}m³ ({o.TempCondition})")
                        .ToList();

                    // Apply to VehicleId
                    ApplyEnum(mediaType.Schema, "VehicleId", vehicles);
                    ApplyEnum(mediaType.Schema, "vehicleId", vehicles);

                    // Apply to OriginWarehouseLocationId
                    ApplyEnum(mediaType.Schema, "OriginWarehouseLocationId", locations);
                    ApplyEnum(mediaType.Schema, "originWarehouseLocationId", locations);

                    // Apply to OrderIds (which is string[] array)
                    ApplyArrayEnum(mediaType.Schema, "OrderIds", orders);
                    ApplyArrayEnum(mediaType.Schema, "orderIds", orders);

                    // Apply datetime examples to PlannedStartTime and PlannedEndTime to help user edit them easily
                    ApplyDateTimeExample(mediaType.Schema, "PlannedStartTime", DateTime.Now.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss"));
                    ApplyDateTimeExample(mediaType.Schema, "plannedStartTime", DateTime.Now.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss"));
                    ApplyDateTimeExample(mediaType.Schema, "PlannedEndTime", DateTime.Now.AddHours(8).ToString("yyyy-MM-ddTHH:mm:ss"));
                    ApplyDateTimeExample(mediaType.Schema, "plannedEndTime", DateTime.Now.AddHours(8).ToString("yyyy-MM-ddTHH:mm:ss"));
                }
            }
            catch (Exception)
            {
                // Silence database errors during Swagger doc build so that Swagger generation doesn't crash the application
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

        private static void ApplyDateTimeExample(OpenApiSchema schema, string propertyName, string exampleValue)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null)
                return;

            property.Type = "string";
            property.Format = "date-time";
            property.Example = new OpenApiString(exampleValue);
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
