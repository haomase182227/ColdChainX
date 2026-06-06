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
        public async Task<IActionResult> GetOrders()
        {
            var result = await _orderService.GetOrdersAsync();
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
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateOrder([FromForm] CreateOrderRequest request)
        {
            var result = await _orderService.CreateOrderAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{orderId:guid}/review")]
        public async Task<IActionResult> ReviewOrder(Guid orderId, [FromBody] ReviewOrderRequest request)
        {
            var result = await _orderService.ReviewOrderAsync(orderId, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
