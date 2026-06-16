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

        /// <summary>
        /// Lấy thông tin hợp đồng (không bao gồm nội dung HTML).
        /// </summary>
        [HttpGet("{contractId:guid}")]
        public async Task<IActionResult> GetContractById(Guid contractId)
        {
            var result = await _contractService.GetContractByIdAsync(contractId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        /// <summary>
        /// Lấy nội dung HTML nháp của hợp đồng dưới dạng raw HTML.
        /// </summary>
        [HttpGet("{contractId:guid}/html")]
        [Produces("text/html")]
        public async Task<IActionResult> GetContractHtml(Guid contractId)
        {
            var result = await _contractService.GetContractHtmlAsync(contractId);
            if (!result.Success) return NotFound(result);
            // Trả raw HTML để tránh JSON serializer escape newline thành \n literal
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
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
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
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
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

            // --- Body là raw HTML (paste trực tiếp không wrap JSON) ---
            // Trường hợp: user paste thẳng HTML vào, không bọc JSON.
            // Ưu tiên check trước để tránh nhầm với JSON khi content-type là application/json.
            var looksLikeRawHtml = trimmedBody.StartsWith('<');
            if (looksLikeRawHtml)
                return trimmedBody;

            // --- application/json hoặc body trông như JSON object ---
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

                    // JSON parse thất bại — có thể user paste HTML chưa escape vào JSON body.
                    // Thử trích xuất phần HTML ra từ value của "editedHtmlContent": "..."
                    if (TryExtractRawHtmlFromMalformedJson(trimmedBody, out var rawHtml))
                        return rawHtml;

                    error = "JSON không hợp lệ. Vui lòng chọn một trong hai cách:\n" +
                            "(1) application/json: body phải là JSON hợp lệ — giá trị HTML bên trong phải escape dấu nháy kép (\\\"...\\\") và newline (\\n).\n" +
                            "(2) text/plain: paste thẳng raw HTML vào body, không cần bọc JSON.";
                    return string.Empty;
                }
            }

            // --- text/html hoặc text/plain ---
            // Swagger UI đôi khi serialize body thành JSON string (bao bởi dấu nháy kép,
            // newline bị escape thành \n) ngay cả khi Content-Type là text/html.
            // Phát hiện trường hợp này và unescape để lấy lại HTML gốc.
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
                    // Không phải JSON string hợp lệ → trả về body gốc bên dưới
                }
            }

            return body;
        }

        /// <summary>
        /// Trích xuất HTML thô từ JSON bị lỗi do HTML chứa newline và dấu nháy kép chưa được escape.
        /// Ví dụ: { "editedHtmlContent": "&lt;!DOCTYPE html&gt;\n&lt;html lang="vi"&gt;..." }
        /// </summary>
        private static bool TryExtractRawHtmlFromMalformedJson(string body, out string html)
        {
            html = string.Empty;

            // Tìm vị trí sau "editedHtmlContent":
            var keyIndex = body.IndexOf("editedHtmlContent", StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0) return false;

            var colonIndex = body.IndexOf(':', keyIndex);
            if (colonIndex < 0) return false;

            // Tìm dấu nháy mở đầu của value
            var openQuote = body.IndexOf('"', colonIndex + 1);
            if (openQuote < 0) return false;

            // HTML bắt đầu từ ký tự sau dấu nháy mở
            var htmlStart = openQuote + 1;
            if (htmlStart >= body.Length) return false;

            // Lấy toàn bộ nội dung từ sau dấu nháy mở đến cuối, bỏ dấu `}` và khoảng trắng cuối
            var rawTail = body[htmlStart..].TrimEnd();

            // Bỏ dấu `}` đóng JSON object ở cuối (nếu có)
            if (rawTail.EndsWith('}'))
                rawTail = rawTail[..^1].TrimEnd();

            // Bỏ dấu nháy đóng của value (nếu có)
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
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
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
        [Authorize(Roles = "Customer")]
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

        [HttpPost("{contractId:guid}/verify")]
        [Authorize(Roles = "Sales,Admin,Dispatcher")]
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
