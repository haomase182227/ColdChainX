using System.Security.Claims;
using System.Text.Json;
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

        [HttpGet]
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
        public async Task<IActionResult> GetContracts(
            [FromQuery] string? status = null,
            [FromQuery] Guid? customerId = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _contractService.GetContractsAsync(
                status,
                customerId,
                fromDate,
                toDate,
                pageNumber,
                pageSize);
            return Ok(result);
        }

        [HttpGet]
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
        public async Task<IActionResult> GetContracts(
            [FromQuery] string? status = null,
            [FromQuery] Guid? customerId = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _contractService.GetContractsAsync(
                status,
                customerId,
                fromDate,
                toDate,
                pageNumber,
                pageSize);
            return Ok(result);
        }

        [HttpGet("{contractId:guid}")]
        public async Task<IActionResult> GetContractById(Guid contractId)
        {
            var result = await _contractService.GetContractByIdAsync(contractId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("by-order/{orderId:guid}")]
        public async Task<IActionResult> GetContractByOrderId(Guid orderId)
        {
            var result = await _contractService.GetContractByOrderIdAsync(orderId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("{contractId:guid}/html")]
        [Produces("text/html")]
        public async Task<IActionResult> GetContractHtml(Guid contractId)
        {
            var result = await _contractService.GetContractHtmlAsync(contractId);
            if (!result.Success) return NotFound(result);
            return Content(result.Data!, "text/html; charset=utf-8");
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
        [Authorize(Roles = "Sales,Customer")]
        public async Task<IActionResult> GenerateContract([FromBody] GenerateContractRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _contractService.GenerateContractAsync(request, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{contractId:guid}")]
        [Consumes("application/json", "text/html", "text/plain")]
        [Authorize(Roles = "Sales,Customer")]
        public async Task<IActionResult> UpdateContractDraft(Guid contractId)
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

            var result = await _contractService.UpdateContractDraftAsync(
                contractId,
                new UpdateContractDraftRequest { EditedHtmlContent = html },
                salesUserId);

            if (!result.Success) return BadRequest(result);
            return Ok(result);
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
                    var request = JsonSerializer.Deserialize<UpdateContractDraftRequest>(
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

        [HttpPost("{contractId:guid}/send")]
        [Authorize(Roles = "Sales,Customer")]
        public async Task<IActionResult> SendContract(Guid contractId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _contractService.SendContractAsync(contractId, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{contractId:guid}/upload-signed")]
        [Authorize(Roles = "Sales,Customer")]
        public async Task<IActionResult> UploadSignedContract(Guid contractId, [FromForm] UploadSignedContractRequest request)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId))
                return Unauthorized("CustomerId claim is missing from token");

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await _contractService.UploadSignedContractAsync(contractId, request, customerId, baseUrl);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{contractId:guid}/review")]
        [Authorize(Roles = "Sales,Customer")]
        public async Task<IActionResult> ReviewContract(Guid contractId, [FromBody] ReviewContractRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _contractService.ReviewContractAsync(contractId, request, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{contractId:guid}/verify")]
        [Authorize(Roles = "Sales,Customer")]
        public async Task<IActionResult> VerifyContract(Guid contractId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var salesUserId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _contractService.VerifyContractAsync(contractId, salesUserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("{contractId:guid}/approve")]
        [Authorize(Roles = "Sales,Customer")]
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
