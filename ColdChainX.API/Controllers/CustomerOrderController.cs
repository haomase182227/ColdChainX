using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.Interfaces;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ColdChainX.API.Controllers
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OrderTabCategory
    {
        IN_STOCK,
        WAITING,
        TRANSIT,
        DELIVERED,
        RETURNED,
        CANCELLED
    }

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
            [FromQuery] OrderTabCategory category = OrderTabCategory.WAITING)
        {
            var customerId = GetCustomerId();
            var query = _dbContext.TransportOrders
                .Include(o => o.DestLocationNavigation)
                .Where(o => o.CustomerId == customerId);

            if (category == OrderTabCategory.IN_STOCK)
            {
                query = query.Where(o => o.Status == "IN_WAREHOUSE" || o.Status == "IN_STOCK");
            }
            else if (category == OrderTabCategory.WAITING)
            {
                query = query.Where(o => o.Status == "LOADING");
            }
            else if (category == OrderTabCategory.TRANSIT)
            {
                query = query.Where(o => o.Status == "IN_TRANSIT" || o.Status == "SEALED" || o.Status == "DISPATCHED");
            }
            else if (category == OrderTabCategory.DELIVERED)
            {
                query = query.Where(o => o.Status == "DELIVERED" || o.Status == "PARTIALLY_DELIVERED");
            }
            else if (category == OrderTabCategory.RETURNED)
            {
                query = query.Where(o => o.Status == "RETURNED" || o.Status == "REJECTED" || o.Status == "RETURN_PENDING" || o.Status == "PENDING_REDELIVERY");
            }
            else if (category == OrderTabCategory.CANCELLED)
            {
                query = query.Where(o => o.Status == "CANCELLED");
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    o.OrderId,
                    o.TrackingCode,
                    o.ItemName,
                    o.Quantity,
                    o.Status,
                    o.CreatedAt,
                    DestinationAddress = o.DestLocationNavigation != null ? o.DestLocationNavigation.Address : "N/A"
                })
                .ToListAsync();

            return Ok(new { success = true, data = orders });
        }

        [HttpGet("{orderId:guid}/tracking-detail")]
        public async Task<IActionResult> GetOrderTrackingDetail(Guid orderId)
        {
            var customerId = GetCustomerId();
            var order = await _dbContext.TransportOrders
                .Include(o => o.DestLocationNavigation)
                .Include(o => o.PickupLocationNavigation)
                .Include(o => o.MasterTrip)
                .Include(o => o.Schedule)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

            if (order == null) return NotFound(new { success = false, message = "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n hÃ ng" });

            DateTime? estimatedDeliveryTime = order.MasterTrip?.PlannedEndTime 
                                              ?? order.Schedule?.DepartureDate;

            return Ok(new
            {
                success = true,
                data = new
                {
                    order.OrderId,
                    order.TrackingCode,
                    order.ItemName,
                    order.Category,
                    order.Quantity,
                    order.Status,
                    OriginAddress = order.PickupLocationNavigation?.Address ?? "N/A",
                    DestinationAddress = order.DestLocationNavigation?.Address ?? "N/A",
                    EstimatedDeliveryTime = estimatedDeliveryTime,
                    CreatedAt = order.CreatedAt
                }
            });
        }


    }
}
