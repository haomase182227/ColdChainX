using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Core.Entities;

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

            if (path == null) return;

            // ── Case 1: plan-load endpoint (POST /api/Dispatch/plan-load) ──────────────────
            if ((string.Equals(path, "api/Dispatch/plan-load", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "api/dispatch/plan-load", StringComparison.OrdinalIgnoreCase))
                && string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
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
                    // Silence DB errors
                }
                return;
            }

            // ── Case 2: path parameter {tripId} endpoints (seal, issue-documents, route-lifo) ──
            if (path.Contains("seal/{tripId}", StringComparison.OrdinalIgnoreCase)
                || path.Contains("issue-documents/{tripId}", StringComparison.OrdinalIgnoreCase)
                || path.Contains("route-lifo/{tripId}", StringComparison.OrdinalIgnoreCase))
            {
                var tripIdParam = operation.Parameters?.FirstOrDefault(p => string.Equals(p.Name, "tripId", StringComparison.OrdinalIgnoreCase));
                if (tripIdParam != null)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                            IQueryable<MasterTrip> query = db.MasterTrips
                                .Include(t => t.Vehicle)
                                .Include(t => t.OriginLocation)
                                .Include(t => t.DestinationLocation);

                            if (path.Contains("seal", StringComparison.OrdinalIgnoreCase))
                            {
                                // Show PLANNED trips to be sealed
                                query = query.Where(t => t.Status == "PLANNED");
                            }
                            else if (path.Contains("issue-documents", StringComparison.OrdinalIgnoreCase))
                            {
                                // Show SEALED trips to issue documents
                                query = query.Where(t => t.Status == "SEALED");
                            }

                            var tripsList = query
                                .OrderByDescending(t => t.CreatedAt)
                                .ToList();

                            var enumValues = tripsList.Select(t => 
                            {
                                var plate = t.Vehicle?.TruckPlate ?? "N/A";
                                var origin = t.OriginLocation?.Address ?? "N/A";
                                var dest = t.DestinationLocation?.Address ?? "N/A";
                                return $"{t.TripId}: Xe {plate} ({origin} -> {dest}) | Status: {t.Status}";
                            }).ToList();

                            tripIdParam.Schema.Type = "string";
                            tripIdParam.Schema.Enum = enumValues.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
                        }
                    }
                    catch (Exception)
                    {
                        // Silence DB errors
                    }
                }
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
