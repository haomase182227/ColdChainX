using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.Interfaces;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/customers/my/orders")]
    [Authorize]
    public class CustomerOrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly ColdChainX.Infrastructure.Persistence.ApplicationDbContext _dbContext;

        public CustomerOrderController(IOrderService orderService, ColdChainX.Infrastructure.Persistence.ApplicationDbContext dbContext)
        {
            _orderService = orderService;
            _dbContext = dbContext;
        }

        private Guid GetCustomerId()
        {
            var customerIdClaim = User.FindFirst("CustomerId");
            if (customerIdClaim != null && Guid.TryParse(customerIdClaim.Value, out var customerId)) return customerId;
            
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var id)) return id;
            throw new UnauthorizedAccessException("Invalid or missing token.");
        }

        [HttpGet]
        public async Task<IActionResult> GetOrdersByCustomer(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null)
        {
            var customerId = GetCustomerId();
            var result = await _orderService.GetOrdersByCustomerAsync(customerId, pageNumber, pageSize, status);
            return Ok(result);
        }


        [HttpGet("by-category")]
        public async Task<IActionResult> GetOrdersByCategory(
            [FromQuery] string category = "IN_STOCK")
        {
            var customerId = GetCustomerId();
            category = category?.Trim()?.ToUpperInvariant() ?? "IN_STOCK";

            var statusList = category switch
            {
                "IN_STOCK" or "0" => new[] { "IN_WAREHOUSE", "IN_STOCK" },
                "WAITING" or "1" => new[] { "LOADING" },
                "TRANSIT" or "2" => new[] { "SEALED", "DISPATCHED", "IN_TRANSIT", "DISPATCHED_PENDING" },
                "DELIVERED" or "3" => new[] { "DELIVERED", "PARTIALLY_DELIVERED" },
                "RETURNED" or "4" => new[] { "RETURNED", "REJECTED", "RETURN_PENDING", "PENDING_REDELIVERY" },
                "CANCELLED" or "5" => new[] { "CANCELLED" },
                _ => (string[]?)null
            };

            if (statusList == null)
                return BadRequest(new { success = false, message = $"Unknown category: '{category}'. Valid: IN_STOCK, WAITING, TRANSIT, DELIVERED, RETURNED, CANCELLED" });

            var normalizedCategory = category switch
            {
                "0" => "IN_STOCK", "1" => "WAITING", "2" => "TRANSIT",
                "3" => "DELIVERED", "4" => "RETURNED", "5" => "CANCELLED",
                _ => category
            };

            var orders = await _dbContext.TransportOrders
                .AsNoTracking()
                .Include(o => o.DestLocationNavigation)
                .Include(o => o.MasterTrip)
                .Where(o => o.CustomerId == customerId && statusList.Contains(o.Status))
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    o.OrderId,
                    o.TrackingCode,
                    o.ItemName,
                    o.Category,
                    o.Quantity,
                    o.Status,
                    o.CreatedAt,
                    DestinationAddress = o.DestLocationNavigation != null ? o.DestLocationNavigation.Address : null,
                    EstimatedArrival = o.MasterTrip != null ? (DateTime?)o.MasterTrip.PlannedEndTime : null
                })
                .ToListAsync();

            return Ok(new { success = true, category = normalizedCategory, data = orders });
        }


        [HttpGet("{orderId:guid}/tracking-detail")]
        public async Task<IActionResult> GetOrderTrackingDetail(Guid orderId)
        {
            var customerId = GetCustomerId();
            var order = await _dbContext.TransportOrders
                .AsNoTracking()
                .Include(o => o.DestLocationNavigation)
                .Include(o => o.PickupLocationNavigation)
                .Include(o => o.OrderDimension)
                .Include(o => o.MasterTrip).ThenInclude(t => t!.Vehicle)
                .Include(o => o.MasterTrip).ThenInclude(t => t!.TripDrivers).ThenInclude(td => td.Driver)
                .Include(o => o.MasterTrip).ThenInclude(t => t!.Route)
                .Include(o => o.MasterTrip).ThenInclude(t => t!.OriginLocation)
                .Include(o => o.MasterTrip).ThenInclude(t => t!.DestinationLocation)
                .Include(o => o.Schedule)
                .Include(o => o.DeliveryEpods).ThenInclude(e => e.ReturnedItems)
                .Include(o => o.WarehouseReceipts).ThenInclude(wr => wr.Warehouse)
                .Include(o => o.Claims)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

            if (order == null)
                return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });

            var result = new Dictionary<string, object?>
            {
                ["orderId"] = order.OrderId,
                ["trackingCode"] = order.TrackingCode,
                ["itemName"] = order.ItemName,
                ["category"] = order.Category,
                ["quantity"] = order.Quantity,
                ["packingType"] = order.PackingType,
                ["tempCondition"] = order.TempCondition,
                ["status"] = order.Status,
                ["createdAt"] = order.CreatedAt,
                ["weightKg"] = order.OrderDimension?.ExpectedWeightKg,
                ["cbmVolume"] = order.OrderDimension?.ExpectedCbm,
                ["originAddress"] = order.PickupLocationNavigation?.Address,
                ["destinationAddress"] = order.DestLocationNavigation?.Address,
            };

            var latestReceipt = order.WarehouseReceipts
                .OrderByDescending(wr => wr.CreatedAt)
                .FirstOrDefault();
            if (latestReceipt != null)
            {
                result["warehouse"] = new
                {
                    latestReceipt.Warehouse.WarehouseId,
                    latestReceipt.Warehouse.WarehouseName,
                    latestReceipt.Warehouse.WarehouseCode,
                    latestReceipt.Warehouse.Address,
                    latestReceipt.Warehouse.WarehouseType,
                    StoredTemperature = latestReceipt.RecordedTemperature,
                    ReceivedAt = latestReceipt.CreatedAt
                };
            }

            if (order.MasterTrip != null)
            {
                var trip = order.MasterTrip;

                result["tripInfo"] = new
                {
                    trip.TripId,
                    trip.SealNumber,
                    trip.PlannedStartTime,
                    trip.PlannedEndTime,
                    DepartedAt = trip.StartedAt,
                    trip.CompletedAt
                };

                if (trip.Vehicle != null)
                {
                    result["vehicle"] = new
                    {
                        trip.Vehicle.TruckPlate,
                        trip.Vehicle.VehicleType,
                        trip.Vehicle.Brand,
                        trip.Vehicle.MaxTemp,
                        trip.Vehicle.MinTemp,
                        trip.Vehicle.CurrentLocation
                    };
                }

                if (trip.TripDrivers?.Any() == true)
                {
                    result["drivers"] = trip.TripDrivers.Select(td => new
                    {
                        td.Driver.FullName,
                        td.Driver.PhoneNumber,
                        td.DriverRole
                    }).ToList();
                }

                if (trip.Route != null)
                {
                    result["route"] = new
                    {
                        trip.Route.RouteCode,
                        trip.Route.OriginCity,
                        trip.Route.DestCity,
                        trip.Route.TransitTime
                    };
                }

                result["estimatedArrival"] = trip.PlannedEndTime;
            }
            else if (order.Schedule != null)
            {
                result["estimatedArrival"] = order.Schedule.DepartureDate;
            }

            var latestEpod = order.DeliveryEpods
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();
            if (latestEpod != null)
            {
                result["delivery"] = new
                {
                    latestEpod.ReceiverName,
                    latestEpod.ReceiverPhone,
                    latestEpod.Note,
                    latestEpod.SignedAt
                };

                var returnedItems = latestEpod.ReturnedItems;
                if (returnedItems?.Any() == true)
                {
                    result["returnedItems"] = returnedItems.Select(ri => new
                    {
                        ri.ReturnId,
                        ri.ItemName,
                        ri.ReasonType,
                        ri.ReasonNote,
                        Quantity = ri.ReturnedQty,
                        ri.ReturnedAt
                    }).ToList();
                }
            }

            if (order.Claims?.Any() == true)
            {
                result["claims"] = order.Claims.Select(c => new
                {
                    c.ClaimCode,
                    c.ClaimType,
                    c.Description,
                    c.Status,
                    c.ResolutionNote,
                    c.CreatedAt,
                    c.ResolvedAt
                }).ToList();
            }

            return Ok(new { success = true, data = result });
        }
    }
}
