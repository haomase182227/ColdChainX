using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ColdChainX.API.Services;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.DTOs.GoogleAuth;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private const string GoogleStateCookiePrefix = ".ColdChainX.GoogleOAuth.";
        private const string GoogleCookiePath = "/api/auth/google";
        private static readonly TimeSpan GoogleStateLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan GoogleLoginCodeLifetime = TimeSpan.FromMinutes(2);

        private readonly IAuthService _authService;
        private readonly IGoogleAuthService _googleAuthService;
        private readonly IGoogleOAuthClient _googleOAuthClient;
        private readonly IGoogleLoginCodeStore _googleLoginCodeStore;
        private readonly ITimeLimitedDataProtector _googleStateProtector;
        private readonly GoogleAuthSettings _googleSettings;
        private readonly IWebHostEnvironment _environment;

        public AuthController(
            IAuthService authService,
            IGoogleAuthService googleAuthService,
            IGoogleOAuthClient googleOAuthClient,
            IGoogleLoginCodeStore googleLoginCodeStore,
            IDataProtectionProvider dataProtectionProvider,
            IOptions<GoogleAuthSettings> googleOptions,
            IWebHostEnvironment environment)
        {
            _authService = authService;
            _googleAuthService = googleAuthService;
            _googleOAuthClient = googleOAuthClient;
            _googleLoginCodeStore = googleLoginCodeStore;
            _googleStateProtector = dataProtectionProvider
                .CreateProtector("ColdChainX.GoogleOAuth.State.v1")
                .ToTimeLimitedDataProtector();
            _googleSettings = googleOptions.Value;
            _environment = environment;
        }

        [HttpPost("register")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Register([FromForm] RegisterRequest request)
        {
            var result = await _authService.RegisterAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            var result = await _authService.GetAllRolesAsync();
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("create-customer")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateCustomer([FromForm] CreateCustomerRequest request)
        {
            var result = await _authService.CreateCustomerAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("create-driver")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateDriver([FromForm] CreateDriverRequest request)
        {
            var result = await _authService.CreateDriverAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("create-warehouse-worker")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateWarehouseWorker([FromForm] CreateWarehouseWorkerRequest request)
        {
            var result = await _authService.CreateWarehouseWorkerAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _authService.LoginAsync(request);
            if (!result.Success) return Unauthorized(result);
            return Ok(result);
        }

        [AllowAnonymous]
        [HttpPost("google-login")]
        [ProducesResponseType(typeof(ApiResponse<GoogleLoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<GoogleLoginResponse>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GoogleLogin(
            [FromBody] GoogleLoginRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _googleAuthService.AuthenticateAsync(
                request.IdToken,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [AllowAnonymous]
        [HttpGet("google-auth")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        public IActionResult GoogleAuth()
        {
            if (!TryGetAbsoluteHttpUri(_googleSettings.RedirectUri, out var redirectUri) ||
                string.IsNullOrWhiteSpace(_googleSettings.ClientId))
            {
                return GoogleConfigurationError(
                    "Google ClientId or RedirectUri is not configured correctly.");
            }

            var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var state = _googleStateProtector.Protect(nonce, GoogleStateLifetime);
            var stateHash = HashGoogleStateValue(nonce);
            var cookieName = BuildGoogleStateCookieName(nonce);

            Response.Cookies.Append(cookieName, stateHash, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = GoogleStateLifetime,
                Path = GoogleCookiePath
            });

            var authorizationUrl = QueryHelpers.AddQueryString(
                "https://accounts.google.com/o/oauth2/v2/auth",
                new Dictionary<string, string?>
                {
                    ["client_id"] = _googleSettings.ClientId,
                    ["redirect_uri"] = redirectUri,
                    ["response_type"] = "code",
                    ["scope"] = "openid email profile",
                    ["state"] = state,
                    ["include_granted_scopes"] = "true",
                    ["prompt"] = "select_account"
                });

            return Redirect(authorizationUrl);
        }

        [AllowAnonymous]
        [HttpGet("google/callback")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        public async Task<IActionResult> GoogleCallback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "Google authorization was denied."));
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return GoogleUnauthorizedResponse("Invalid Google OAuth callback.");

            if (!TryValidateGoogleState(state))
                return GoogleUnauthorizedResponse("Invalid or expired Google OAuth state.");

            if (!TryGetAbsoluteHttpUri(_googleSettings.RedirectUri, out var redirectUri))
                return GoogleConfigurationError("Google RedirectUri is not configured correctly.");

            var idToken = await _googleOAuthClient.ExchangeCodeForIdTokenAsync(
                code,
                redirectUri,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(idToken))
                return GoogleUnauthorizedResponse("Google did not return a valid ID token.");

            var result = await _googleAuthService.AuthenticateAsync(idToken, cancellationToken);
            if (!result.Success || result.Data == null)
                return StatusCode(result.StatusCode, result);

            if (_environment.IsDevelopment() && _googleSettings.BackendTestCallbackEnabled)
                return BuildGoogleDevelopmentResultPage(result.Data);

            if (!TryGetAbsoluteHttpUri(
                    _googleSettings.FrontendCallbackUrl,
                    out var frontendCallbackUrl))
            {
                return GoogleConfigurationError(
                    "Google FrontendCallbackUrl is not configured correctly.");
            }

            var oneTimeCode = WebEncoders.Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));
            await _googleLoginCodeStore.StoreAsync(
                oneTimeCode,
                result.Data,
                GoogleLoginCodeLifetime,
                cancellationToken);

            var frontendUrl = QueryHelpers.AddQueryString(
                frontendCallbackUrl,
                "code",
                oneTimeCode);

            return Redirect(frontendUrl);
        }

        [AllowAnonymous]
        [HttpPost("google/exchange")]
        [ProducesResponseType(typeof(ApiResponse<GoogleLoginResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<GoogleLoginResponse>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ExchangeGoogleLoginCode(
            [FromBody] GoogleExchangeRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Unauthorized(ApiResponse<GoogleLoginResponse>.Failure(
                    "Invalid or expired one-time login code.",
                    StatusCodes.Status401Unauthorized));
            }

            var authentication = await _googleLoginCodeStore.TakeAsync(
                request.Code,
                cancellationToken);

            if (authentication == null)
            {
                return Unauthorized(ApiResponse<GoogleLoginResponse>.Failure(
                    "Invalid or expired one-time login code.",
                    StatusCodes.Status401Unauthorized));
            }

            return Ok(ApiResponse<GoogleLoginResponse>.SuccessResponse(
                authentication,
                "Google login code exchanged successfully"));
        }

        [HttpPost("refresh-tokens")]
        public async Task<IActionResult> Refresh([FromBody] string refreshToken)
        {
            var result = await _authService.RefreshTokensAsync(refreshToken);
            if (!result.Success) return Unauthorized(result);
            return Ok(result);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                              ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(ApiResponse<bool>.Failure("Invalid token"));

            var result = await _authService.LogoutAsync(userId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [Authorize]
        [HttpPut("change-password")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                              ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(ApiResponse<bool>.Failure("Invalid token", StatusCodes.Status401Unauthorized));

            var result = await _authService.ChangePasswordAsync(userId, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserRequest request)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                              ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(ApiResponse<bool>.Failure("Invalid token"));

            var result = await _authService.UpdateUserAsync(userId, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpPatch("update-driver/{driverId:guid}/fullname")]
        public async Task<IActionResult> UpdateDriverFullName(Guid driverId, [FromBody] UpdateDriverFullNameRequest request)
        {
            var mapped = new UpdateDriverInfoRequest { FullName = request.FullName };
            var result = await _authService.UpdateDriverAsync(driverId, mapped);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

    

        [Authorize(Roles = "Admin")]
        [HttpPatch("update-driver/{driverId:guid}/date-of-birth")]
        public async Task<IActionResult> UpdateDriverDob(Guid driverId, [FromBody] UpdateDriverDobRequest request)
        {
            var mapped = new UpdateDriverInfoRequest { DateOfBirth = request.DateOfBirth };
            var result = await _authService.UpdateDriverAsync(driverId, mapped);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        

        [Authorize(Roles = "Admin")]
        [HttpPatch("update-driver/{driverId:guid}/license")]
        public async Task<IActionResult> UpdateDriverLicense(Guid driverId, [FromBody] UpdateDriverLicenseRequest request)
        {
            var mapped = new UpdateDriverInfoRequest
            {
                LicenseNumber = request.LicenseNumber,
                LicenseClass = request.LicenseClass,
                IssueDate = request.IssueDate,
                ExpiryDate = request.ExpiryDate,
                DocumentUrl = request.DocumentUrl
            };
            var result = await _authService.UpdateDriverAsync(driverId, mapped);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> SoftDeleteUser(Guid id)
        {
            var result = await _authService.SoftDeleteUserAsync(id);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        private bool TryValidateGoogleState(string protectedState)
        {
            string nonce;
            try
            {
                nonce = _googleStateProtector.Unprotect(protectedState);
            }
            catch (CryptographicException)
            {
                return false;
            }

            var cookieName = BuildGoogleStateCookieName(nonce);
            if (!Request.Cookies.TryGetValue(cookieName, out var cookieValue))
                return false;

            Response.Cookies.Delete(cookieName, new CookieOptions
            {
                Path = GoogleCookiePath
            });

            var expectedBytes = Encoding.UTF8.GetBytes(HashGoogleStateValue(nonce));
            var actualBytes = Encoding.UTF8.GetBytes(cookieValue);
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }

        private IActionResult BuildGoogleDevelopmentResultPage(
            GoogleLoginResponse authentication)
        {
            var encoder = HtmlEncoder.Default;
            var html = $$"""
                <!doctype html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>ColdChainX Google Login</title>
                </head>
                <body>
                    <h1>Login successful</h1>
                    <p>User: {{encoder.Encode(authentication.User.FullName)}}</p>
                    <p>Email: {{encoder.Encode(authentication.User.Email ?? string.Empty)}}</p>
                    <p>Role: {{encoder.Encode(authentication.User.Role ?? string.Empty)}}</p>
                    <label for="token">Application JWT</label>
                    <textarea id="token" rows="12" cols="100" readonly>{{encoder.Encode(authentication.Token)}}</textarea>
                </body>
                </html>
                """;

            return Content(html, "text/html; charset=utf-8");
        }

        private ObjectResult GoogleConfigurationError(string message)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Failure(
                    message,
                    StatusCodes.Status500InternalServerError));
        }

        private UnauthorizedObjectResult GoogleUnauthorizedResponse(string message)
        {
            return Unauthorized(ApiResponse<object>.Failure(
                message,
                StatusCodes.Status401Unauthorized));
        }

        private static string BuildGoogleStateCookieName(string nonce)
            => GoogleStateCookiePrefix + HashGoogleStateValue(nonce)[..16];

        private static string HashGoogleStateValue(string value)
            => Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();

        private static bool TryGetAbsoluteHttpUri(string? value, out string uri)
        {
            uri = string.Empty;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            uri = parsed.AbsoluteUri;
            return true;
        }

    }
}
