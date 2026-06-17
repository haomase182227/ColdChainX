using ColdChainX.Application.DTOs.Asns;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/asns")]
    public class AsnController : ControllerBase
    {
        private readonly IAsnService _asnService;

        public AsnController(IAsnService asnService)
        {
            _asnService = asnService;
        }

        [HttpPost]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CreateAsn([FromBody] CreateAsnRequest request)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _asnService.CreateAsnAsync(request, customerId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
