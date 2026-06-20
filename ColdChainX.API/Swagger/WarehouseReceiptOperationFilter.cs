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
    public class WarehouseReceiptOperationFilter : IOperationFilter
    {
        private readonly IServiceProvider _serviceProvider;

        public WarehouseReceiptOperationFilter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.TrimEnd('/');
            if (path == null) return;

            // Route matching for api/v1/warehouse-receipts/orders/{orderId}/qc or completion
            if (path.StartsWith("api/v1/warehouse-receipts/orders/{orderId}", StringComparison.OrdinalIgnoreCase))
            {
                var orderIdParam = operation.Parameters?.FirstOrDefault(p => string.Equals(p.Name, "orderId", StringComparison.OrdinalIgnoreCase));
                if (orderIdParam != null)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var orders = db.TransportOrders
                                .OrderByDescending(o => o.CreatedAt)
                                .Select(o => $"{o.OrderId}: {o.TrackingCode} - {o.ItemName} ({o.Status})")
                                .ToList();

                            if (!orders.Any())
                            {
                                orders.Add("EMPTY: Không có đơn hàng nào trong hệ thống");
                            }

                            orderIdParam.Schema.Type = "string";
                            orderIdParam.Schema.Enum = orders.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
                        }
                    }
                    catch { }
                }

                var warehouseIdParam = operation.Parameters?.FirstOrDefault(p => string.Equals(p.Name, "warehouseId", StringComparison.OrdinalIgnoreCase));
                if (warehouseIdParam != null)
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var warehouses = db.Warehouses
                                .Where(w => w.Status == "ACTIVE")
                                .Select(w => $"{w.WarehouseId}: {w.WarehouseName}")
                                .ToList();

                            if (!warehouses.Any())
                            {
                                warehouses.Add("EMPTY: Không có kho hàng nào khả dụng");
                            }

                            warehouseIdParam.Schema.Type = "string";
                            warehouseIdParam.Schema.Enum = warehouses.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
