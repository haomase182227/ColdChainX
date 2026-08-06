using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/customers")]
    public class CustomerController : ControllerBase
    {
        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;

        public CustomerController(
            ICustomerService customerService,
            IOrderService orderService)
        {
            _customerService = customerService;
            _orderService = orderService;
        }

        [HttpGet]
        public async Task<IActionResult> GetCustomers([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _customerService.GetCustomersAsync(pageNumber, pageSize);
            return Ok(result);
        }

        [HttpGet("{customerId:guid}")]
        public async Task<IActionResult> GetCustomerById(Guid customerId)
        {
            var result = await _customerService.GetCustomerByIdAsync(customerId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("{customerId:guid}/orders")]
        [Authorize(Roles = "Sales,Admin,WarehouseWorker,Customer")]
        public async Task<IActionResult> GetCustomerOrders(
            Guid customerId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 30,
            [FromQuery] string? status = null)
        {
            if (User.IsInRole("Customer"))
            {
                var customerIdClaim = User.FindFirst("CustomerId")?.Value;
                if (!Guid.TryParse(customerIdClaim, out var requesterCustomerId))
                    return Unauthorized("CustomerId claim is missing from token");

                if (requesterCustomerId != customerId)
                    return Forbid();
            }

            var result = await _orderService.GetOrdersByCustomerAsync(
                customerId,
                pageNumber,
                pageSize,
                status);

            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}
