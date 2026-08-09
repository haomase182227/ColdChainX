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
using System.Security.Claims;
using System;

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

    [HttpPost("/api/payments/bank-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceivePaymentWebhook([FromBody] PaymentWebhookRequest request)
    {
        string? rawBody = null;
        string? payOsSignature = Request.Headers["x-payos-signature"].FirstOrDefault();

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

    [HttpGet("/api/payments/transactions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllPaymentTransactions(
        [FromQuery] string? status = null,
        [FromQuery] string? transactionType = null,
        [FromQuery] string? paymentMethod = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10)
    {
        if (pageNumber <= 0 || pageSize <= 0)
            return BadRequest(ApiResponse<object>.Failure("PageNumber and PageSize must be greater than zero."));

        var result = await _mediator.Send(new ColdChainX.Application.Features.Payment.Queries.GetAllPaymentTransactionsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            Status = status,
            TransactionType = transactionType,
            PaymentMethod = paymentMethod,
            FromDate = fromDate,
            ToDate = toDate
        });
        return Ok(result);
    }

    [HttpGet("/api/payments/transactions/customer/me")]
    [HttpGet("/api/payments/transactions/customer/{customerId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetCustomerPaymentTransactions([FromRoute] Guid? customerId = null)
    {
        var targetId = customerId;
        if (!targetId.HasValue || targetId.Value == Guid.Empty)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(customerIdClaim) || !Guid.TryParse(customerIdClaim, out var parsedId))
            {
                return Unauthorized(ApiResponse<object>.Failure("KhÃ´ng thá»ƒ xÃ¡c thá»±c danh tÃ­nh khÃ¡ch hÃ ng tá»« token."));
            }
            targetId = parsedId;
        }

        var result = await _mediator.Send(new ColdChainX.Application.Features.Payment.Queries.GetCustomerPaymentTransactionsQuery { CustomerId = targetId.Value });
        if (!result.Success)
        {
            return StatusCode(result.StatusCode != 0 ? result.StatusCode : StatusCodes.Status404NotFound, result);
        }
        return Ok(result);
    }

    [HttpGet("/api/payments/invoices/{referenceId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPaymentInvoiceById(Guid referenceId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Payment.Queries.GetPaymentInvoiceQuery { ReferenceId = referenceId });
        return Ok(result);
    }

}

