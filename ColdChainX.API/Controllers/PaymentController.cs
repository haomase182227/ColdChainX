using System.IO;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ColdChainX.Application.DTOs.Payment;
using ColdChainX.Application.Features.Payment.Commands;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers;

/// <summary>
/// Handle payment webhooks from PayOS.
/// </summary>
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
    /// Receive PayOS payment webhook notification.
    /// </summary>
    [HttpPost("/api/payments/bank-webhook")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReceivePaymentWebhook([FromBody] PaymentWebhookRequest request)
    {
        // Read raw body for HMAC verification (must be done before model binding, but we use EnableBuffering)
        string? rawBody = null;
        string? payOsSignature = Request.Headers["x-payos-signature"].FirstOrDefault();

        // Re-read body if buffered (requires app.Use(EnableBuffering) middleware or UseBufferedBodyReading)
        if (Request.Body.CanSeek)
        {
            Request.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
            rawBody = await reader.ReadToEndAsync();
        }

        var command = new ReceivePaymentWebhookCommand
        {
            Request = request,
            PayOsSignature = payOsSignature,
            RawBody = rawBody
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
