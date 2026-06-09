using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/customers/{customerId:guid}/orders")]
    public class CustomerOrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public CustomerOrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrdersByCustomer(
            Guid customerId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _orderService.GetOrdersByCustomerAsync(customerId, pageNumber, pageSize);
            return Ok(result);
        }
    }
}
