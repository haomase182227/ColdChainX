using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Payment;
using ColdChainX.Application.Features.Payment.Commands;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Nhận thông báo thanh toán chuyển khoản thành công từ Ngân hàng (Webhook).
    /// </summary>
    [HttpPost("/api/payments/bank-webhook")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReceivePaymentWebhook([FromBody] PaymentWebhookRequest request)
    {
        var command = new ReceivePaymentWebhookCommand { Request = request };
        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
