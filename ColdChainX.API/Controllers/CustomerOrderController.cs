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
        public async Task<IActionResult> GetOrdersByCustomer(Guid customerId)
        {
            var result = await _orderService.GetOrdersByCustomerAsync(customerId);
            return Ok(result);
        }
    }
}
