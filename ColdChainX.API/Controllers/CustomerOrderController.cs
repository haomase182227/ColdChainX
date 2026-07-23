using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.Interfaces;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/customers/{customerId:guid}/orders")]
    public class CustomerOrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly ColdChainX.Infrastructure.Persistence.ApplicationDbContext _dbContext;

        public CustomerOrderController(IOrderService orderService, ColdChainX.Infrastructure.Persistence.ApplicationDbContext dbContext)
        {
            _orderService = orderService;
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrdersByCustomer(
            Guid customerId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null)
        {
            var result = await _orderService.GetOrdersByCustomerAsync(customerId, pageNumber, pageSize, status);
            return Ok(result);
        }

        [HttpGet("by-category")]
        public async Task<IActionResult> GetOrdersByCategory(
            Guid customerId, 
            [FromQuery] string category = "WAITING")
        {
            var query = _dbContext.TransportOrders
                .Include(o => o.DestLocationNavigation)
                .Where(o => o.CustomerId == customerId);

            category = category.ToUpper();
            if (category == "IN_STOCK" || category == "IN-STOCK")
            {
                query = query.Where(o => o.Status == "IN-STOCK");
            }
            else if (category == "WAITING")
            {
                query = query.Where(o => o.Status == "LOADING");
            }
            else if (category == "TRANSIT")
            {
                query = query.Where(o => o.Status == "IN-TRANSIT");
            }
            else if (category == "DELIVERED")
            {
                query = query.Where(o => o.Status == "DELIVERED");
            }
            else if (category == "RETURNED")
            {
                query = query.Where(o => o.Status == "RETURNED" || o.Status == "REJECTED" || o.Status == "RETURN_PENDING");
            }
            else if (category == "CANCELLED")
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
        public async Task<IActionResult> GetOrderTrackingDetail(Guid customerId, Guid orderId)
        {
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
