using System;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/contracts/appendices")]
    public class ContractAppendicesController : ControllerBase
    {
        private readonly IContractAppendixService _appendixService;

        public ContractAppendicesController(IContractAppendixService appendixService)
        {
            _appendixService = appendixService;
        }

        [HttpGet("preview")]
        [Produces("text/html")]
        [Authorize(Roles = "Sales,Admin,Manager,Dispatcher")]
        public async Task<IActionResult> PreviewAppendix(
            [FromQuery] Guid orderId,
            [FromQuery] decimal adjustedPrice,
            [FromQuery] string reason)
        {
            var result = await _appendixService.PreviewAppendixAsync(orderId, adjustedPrice, reason);
            if (!result.Success) return BadRequest(result);
            return Content(result.Data!, "text/html; charset=utf-8");
        }

        [HttpPost("generate")]
        [Authorize(Roles = "Sales,Admin,Manager,Dispatcher")]
        public async Task<IActionResult> GenerateAppendix([FromBody] GenerateAppendixRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _appendixService.GenerateAppendixAsync(request.OrderId, request.AdjustedPrice, request.Reason, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{appendixId:guid}")]
        [Consumes("application/json", "text/html", "text/plain")]
        [Authorize(Roles = "Sales,Admin,Manager,Dispatcher")]
        public async Task<IActionResult> UpdateAppendixDraft(Guid appendixId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var contentType = Request.ContentType ?? string.Empty;
            var html = ReadEditedHtmlContent(body, contentType, out var parseError);
            if (parseError != null)
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = parseError,
                    Data = (object?)null
                });
            }

            var result = await _appendixService.UpdateAppendixDraftAsync(appendixId, html, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{appendixId:guid}/send")]
        [Authorize(Roles = "Sales,Admin,Manager,Dispatcher")]
        public async Task<IActionResult> SendAppendix(Guid appendixId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _appendixService.SendAppendixAsync(appendixId, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{appendixId:guid}/accept")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> AcceptAppendix(Guid appendixId)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _appendixService.AcceptAppendixAsync(appendixId, customerId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{appendixId:guid}/reject")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> RejectAppendix(Guid appendixId)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var result = await _appendixService.RejectAppendixAsync(appendixId, customerId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{appendixId:guid}/execute")]
        [Authorize(Roles = "Sales,Admin,Manager,Dispatcher")]
        public async Task<IActionResult> ExecuteAppendixResolution(Guid appendixId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _appendixService.ExecuteAppendixResolutionAsync(appendixId, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpGet("{appendixId:guid}")]
        [Authorize]
        public async Task<IActionResult> GetAppendixById(Guid appendixId)
        {
            var result = await _appendixService.GetAppendixByIdAsync(appendixId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("{appendixId:guid}/html")]
        [Produces("text/html")]
        [Authorize]
        public async Task<IActionResult> GetAppendixHtml(Guid appendixId)
        {
            var result = await _appendixService.GetAppendixHtmlAsync(appendixId);
            if (!result.Success) return NotFound(result);
            return Content(result.Data!, "text/html; charset=utf-8");
        }

        private static string ReadEditedHtmlContent(string body, string contentType, out string? error)
        {
            error = null;
            var trimmedBody = body.Trim();

            var looksLikeRawHtml = trimmedBody.StartsWith('<');
            if (looksLikeRawHtml)
                return trimmedBody;

            var looksLikeJsonWrapper = trimmedBody.StartsWith('{')
                && trimmedBody.Contains("editedHtmlContent", StringComparison.OrdinalIgnoreCase);

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) || looksLikeJsonWrapper)
            {
                try
                {
                    var request = JsonSerializer.Deserialize<UpdateAppendixDraftRequest>(
                        body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    return request?.EditedHtmlContent ?? string.Empty;
                }
                catch (JsonException)
                {
                    if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                        && TryExtractEditedHtmlContentFromLooseWrapper(trimmedBody, out var html))
                    {
                        return html;
                    }

                    if (TryExtractRawHtmlFromMalformedJson(trimmedBody, out var rawHtml))
                        return rawHtml;

                    error = "JSON không hợp lệ. Vui lòng chọn một trong hai cách:\n" +
                            "(1) application/json: body phải là JSON hợp lệ — giá trị HTML bên trong phải escape dấu nháy kép (\\\"...\\\") và newline (\\n).\n" +
                            "(2) text/plain: paste thẳng raw HTML vào body, không cần bọc JSON.";
                    return string.Empty;
                }
            }

            if (trimmedBody.StartsWith('"') && trimmedBody.EndsWith('"') && trimmedBody.Length >= 2)
            {
                try
                {
                    var unescaped = JsonSerializer.Deserialize<string>(trimmedBody);
                    if (!string.IsNullOrWhiteSpace(unescaped))
                        return unescaped;
                }
                catch (JsonException)
                {
                }
            }

            return body;
        }

        private static bool TryExtractRawHtmlFromMalformedJson(string body, out string html)
        {
            html = string.Empty;

            var keyIndex = body.IndexOf("editedHtmlContent", StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0) return false;

            var colonIndex = body.IndexOf(':', keyIndex);
            if (colonIndex < 0) return false;

            var openQuote = body.IndexOf('"', colonIndex + 1);
            if (openQuote < 0) return false;

            var htmlStart = openQuote + 1;
            if (htmlStart >= body.Length) return false;

            var rawTail = body[htmlStart..].TrimEnd();

            if (rawTail.EndsWith('}'))
                rawTail = rawTail[..^1].TrimEnd();

            if (rawTail.EndsWith('"'))
                rawTail = rawTail[..^1];

            if (!rawTail.TrimStart().StartsWith('<'))
                return false;

            html = rawTail.Trim();
            return !string.IsNullOrWhiteSpace(html);
        }

        private static bool TryExtractEditedHtmlContentFromLooseWrapper(string value, out string html)
        {
            html = string.Empty;

            var propertyIndex = value.IndexOf("editedHtmlContent", StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
                return false;

            var colonIndex = value.IndexOf(':', propertyIndex);
            if (colonIndex < 0)
                return false;

            var firstQuoteIndex = value.IndexOf('"', colonIndex + 1);
            var lastBraceIndex = value.LastIndexOf('}');
            var lastQuoteIndex = lastBraceIndex > firstQuoteIndex
                ? value.LastIndexOf('"', lastBraceIndex - 1)
                : value.LastIndexOf('"');

            if (firstQuoteIndex < 0 || lastQuoteIndex <= firstQuoteIndex)
                return false;

            var quotedJsonString = value[firstQuoteIndex..(lastQuoteIndex + 1)];
            try
            {
                html = JsonSerializer.Deserialize<string>(quotedJsonString) ?? string.Empty;
            }
            catch (JsonException)
            {
                html = value[(firstQuoteIndex + 1)..lastQuoteIndex]
                    .Replace("\\r\\n", Environment.NewLine)
                    .Replace("\\n", Environment.NewLine)
                    .Replace("\\r", Environment.NewLine)
                    .Replace("\\\"", "\"")
                    .Replace("\\/", "/")
                    .Replace("\\\\", "\\");
            }

            return true;
        }
    }
}
