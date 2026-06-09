using System.Security.Claims;
using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/contracts")]
    public class ContractsController : ControllerBase
    {
        private readonly IContractService _contractService;

        public ContractsController(IContractService contractService)
        {
            _contractService = contractService;
        }

        [HttpGet("preview/{orderId:guid}")]
        [Produces("text/html")]
        public async Task<IActionResult> PreviewContract(Guid orderId)
        {
            var result = await _contractService.PreviewContractAsync(orderId);
            if (!result.Success) return BadRequest(result);
            return Content(result.Data!, "text/html; charset=utf-8");
        }

        [HttpPost("generate")]
        [Authorize(Roles = "Sales,Admin,Manager")]
        public async Task<IActionResult> GenerateContract([FromBody] GenerateContractRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _contractService.GenerateContractAsync(request, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{contractId:guid}/approve")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> ApproveContract(Guid contractId)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _contractService.ApproveContractAsync(contractId, customerId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
