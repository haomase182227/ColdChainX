using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? status = null)
        {
            var result = await _orderService.GetOrdersAsync(pageNumber, pageSize, status);
            return Ok(result);
        }

        [HttpGet("{orderId}/origin-warehouses")]
        public async Task<IActionResult> GetOriginWarehouses(Guid orderId)
        {
            var result = await _orderService.GetOriginWarehousesForOrderAsync(orderId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("my-orders")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetMyOrders([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? status = null)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _orderService.GetOrdersByCustomerAsync(customerId, pageNumber, pageSize, status);
            return Ok(result);
        }

        [HttpGet("{orderId:guid}")]
        public async Task<IActionResult> GetOrderById(Guid orderId)
        {
            var result = await _orderService.GetOrderByIdAsync(orderId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "Customer")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateOrder([FromForm] CreateOrderRequest request)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _orderService.CreateOrderAsync(request, customerId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{orderId:guid}")]
        [Authorize(Roles = "Customer")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateOrder(Guid orderId, [FromForm] UpdateOrderRequest request)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _orderService.UpdateOrderAsync(orderId, request, customerId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{orderId:guid}/admin")]
        [Authorize(Roles = "Admin,Manager,Sales")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AdminUpdateOrder(Guid orderId, [FromForm] UpdateOrderRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _orderService.AdminUpdateOrderAsync(orderId, request, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{orderId:guid}/review")]
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
        public async Task<IActionResult> ReviewOrder(Guid orderId, [FromBody] ReviewOrderRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _orderService.ReviewOrderAsync(orderId, request, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
