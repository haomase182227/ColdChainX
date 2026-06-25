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
    public class DispatchOperationFilter : IOperationFilter
    {
        private readonly IServiceProvider _serviceProvider;

        public DispatchOperationFilter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            if (path == null) return;

            // Forms payload dropdowns
            if (path.StartsWith("api/Dispatch", StringComparison.OrdinalIgnoreCase))
            {
                if (operation.RequestBody?.Content != null && operation.RequestBody.Content.TryGetValue("multipart/form-data", out var mediaType) && mediaType.Schema != null)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                            // Locations: Status = ACTIVE
                            var locations = db.Locations
                                .Where(l => l.Status == "ACTIVE")
                                .OrderBy(l => l.Address)
                                .Select(l => $"{l.LocationId}: {l.Address}")
                                .ToList();



                            
var rawOrders = (from r in db.WarehouseReceipts
                                             join o in db.TransportOrders on r.OrderId equals o.OrderId
                                             join w in db.Warehouses on r.WarehouseId equals w.WarehouseId
                                             join c in db.Customers on o.CustomerId equals c.CustomerId into cg
                                             from cust in cg.DefaultIfEmpty()
                                             select new {
                                                 o.OrderId,
                                                 o.TrackingCode,
                                                 o.ItemName,
                                                 o.CustomerId,
                                                 Weight = o.ActualWeightKg > 0 ? o.ActualWeightKg : o.ExpectedWeightKg,
                                                 o.TempCondition,
                                                 CustomerName = cust != null ? cust.CompanyName : "N/A",
                                                 WarehouseName = w.WarehouseName
                                             })
                                             .ToList();

                            var orders = rawOrders.Select(x => {
                                var locCode = "RCV-STAGE-01";
                                return $"{x.OrderId}: {x.TrackingCode} - {x.ItemName} ({x.Weight}kg, {x.TempCondition}) | Khách: {x.CustomerName} | Kho: {x.WarehouseName} (Vị trí: {locCode})";
                            }).ToList();

                            ApplyArrayEnum(mediaType.Schema, "OrderIds", orders);
                            ApplyArrayEnum(mediaType.Schema, "orderIds", orders);

                            var vehicles = db.Vehicles
                                .Where(v => v.Status == "ACTIVE")
                                .Select(v => $"{v.VehicleId}: {v.TruckPlate} — {v.VehicleType} | Tải: {v.MaxWeight}kg | Temp: {v.MinTemp}°C đến {v.MaxTemp}°C")
                                .ToList();

                            ApplyEnum(mediaType.Schema, "VehicleId", vehicles);
                            ApplyEnum(mediaType.Schema, "vehicleId", vehicles);

                            // Tài xế khả dụng (không RELAX/Offline/Inactive) — chọn 1–2 người cho chuyến
                            var driversList = db.Drivers
                                .Where(d => d.Status != "RELAX" && d.Status != "Offline"
                                         && d.Status != "Inactive" && d.Status != "DELETED")
                                .OrderBy(d => d.FullName)
                                .Select(d => $"{d.DriverId}: {d.FullName} — {d.PhoneNumber} ({d.Status})")
                                .ToList();

                            ApplyArrayEnum(mediaType.Schema, "DriverIds", driversList);
                            ApplyArrayEnum(mediaType.Schema, "driverIds", driversList);

                            ApplyDateTimeExample(mediaType.Schema, "PlannedStartTime", DateTime.Now.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss"));
                            ApplyDateTimeExample(mediaType.Schema, "plannedStartTime", DateTime.Now.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss"));
                            ApplyDateTimeExample(mediaType.Schema, "PlannedEndTime", DateTime.Now.AddHours(8).ToString("yyyy-MM-ddTHH:mm:ss"));
                            ApplyDateTimeExample(mediaType.Schema, "plannedEndTime", DateTime.Now.AddHours(8).ToString("yyyy-MM-ddTHH:mm:ss"));
                        }
                    }
                    catch (Exception) { /* Silence */ }
                }

                // Một số endpoint cần NHẬP TAY tripId (không dùng dropdown) — FE lấy id từ
                // GET trips/can-start-picking hoặc GET trips/ready-to-seal rồi nhập vào.
                var freeTextTripId =
                    path.Contains("start-picking", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("seal-and-dispatch", StringComparison.OrdinalIgnoreCase);

                // Path parameter dropdowns
                var tripIdParam = operation.Parameters?.FirstOrDefault(p => string.Equals(p.Name, "tripId", StringComparison.OrdinalIgnoreCase));
                if (tripIdParam != null && !freeTextTripId)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var tripsList = db.MasterTrips
                                .Include(t => t.Vehicle)
                                .Include(t => t.OriginLocation)
                                .Include(t => t.DestinationLocation)
                                .OrderByDescending(t => t.CreatedAt)
                                .ToList();

                            var enumValues = tripsList.Select(t => 
                                $"{t.TripId}: Xe {t.Vehicle?.TruckPlate ?? "N/A"} | Status: {t.Status}").ToList();

                            tripIdParam.Schema.Type = "string";
                            tripIdParam.Schema.Enum = enumValues.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
                        }
                    }
                    catch (Exception) { /* Silence */ }
                }

                var vehicleIdParam = operation.Parameters?.FirstOrDefault(p => string.Equals(p.Name, "vehicleId", StringComparison.OrdinalIgnoreCase));
                if (vehicleIdParam != null)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var vehicles = db.Vehicles
                                .Where(v => v.Status == "ACTIVE")
                                .Select(v => $"{v.VehicleId}: {v.TruckPlate} — {v.VehicleType} | Tải: {v.MaxWeight}kg | Temp: {v.MinTemp}°C đến {v.MaxTemp}°C")
                                .ToList();

                            vehicleIdParam.Schema.Type = "string";
                            vehicleIdParam.Schema.Enum = vehicles.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
                        }
                    }
                    catch (Exception) { /* Silence */ }
                }

                var orderIdsParam = operation.Parameters?.FirstOrDefault(p => string.Equals(p.Name, "orderIds", StringComparison.OrdinalIgnoreCase));
                if (orderIdsParam != null)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            
                            
var rawOrders = (from r in db.WarehouseReceipts
                                              join o in db.TransportOrders on r.OrderId equals o.OrderId
                                              join w in db.Warehouses on r.WarehouseId equals w.WarehouseId
                                              join c in db.Customers on o.CustomerId equals c.CustomerId into cg
                                              from cust in cg.DefaultIfEmpty()
                                              select new {
                                                  o.OrderId,
                                                  o.TrackingCode,
                                                  o.ItemName,
                                                  o.CustomerId,
                                                  Weight = o.ActualWeightKg > 0 ? o.ActualWeightKg : o.ExpectedWeightKg,
                                                  o.TempCondition,
                                                  CustomerName = cust != null ? cust.CompanyName : "N/A",
                                                  WarehouseName = w.WarehouseName
                                              })
                                              .ToList();

                             var orders = rawOrders.Select(x => {
                                 var locCode = "RCV-STAGE-01";
                                 return $"{x.OrderId}: {x.TrackingCode} - {x.ItemName} ({x.Weight}kg, {x.TempCondition}) | Khách: {x.CustomerName} | Kho: {x.WarehouseName} (Vị trí: {locCode})";
                             }).ToList();

                             if (!orders.Any())
                             {
                                 orders.Add("EMPTY: Không có đơn hàng nào trong WarehouseReceipts");
                             }

                            orderIdsParam.Schema.Items.Type = "string";
                            orderIdsParam.Schema.Items.Enum = orders.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
                        }
                    }
                    catch { }
                }
            }
        }

        private static void ApplyEnum(OpenApiSchema schema, string propertyName, IEnumerable<string> values)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null) return;
            property.Type = "string";
            property.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }

        private static void ApplyArrayEnum(OpenApiSchema schema, string propertyName, IEnumerable<string> values)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null) return;
            property.Type = "array";
            if (property.Items == null) property.Items = new OpenApiSchema();
            property.Items.Type = "string";
            property.Items.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }

        private static void ApplyDateTimeExample(OpenApiSchema schema, string propertyName, string exampleValue)
        {
            var property = FindProperty(schema, propertyName);
            if (property == null) return;
            property.Type = "string";
            property.Format = "date-time";
            property.Example = new OpenApiString(exampleValue);
        }

        private static OpenApiSchema? FindProperty(OpenApiSchema schema, string propertyName)
        {
            if (schema.Properties.TryGetValue(propertyName, out var exactMatch)) return exactMatch;
            return schema.Properties.FirstOrDefault(entry => string.Equals(entry.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Value;
        }
    }
}

