using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ColdChainX.API.Services;
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
    [AllowAnonymous]
    [Route("api/Accounts")]
    public class AccountsController : ControllerBase
    {
        private const string StateCookiePrefix = ".ColdChainX.GoogleOAuth.";
        private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan LoginCodeLifetime = TimeSpan.FromMinutes(2);

        private readonly IGoogleAuthService _googleAuthService;
        private readonly IGoogleOAuthClient _googleOAuthClient;
        private readonly IGoogleLoginCodeStore _loginCodeStore;
        private readonly ITimeLimitedDataProtector _stateProtector;
        private readonly GoogleAuthSettings _settings;
        private readonly IWebHostEnvironment _environment;

        public AccountsController(
            IGoogleAuthService googleAuthService,
            IGoogleOAuthClient googleOAuthClient,
            IGoogleLoginCodeStore loginCodeStore,
            IDataProtectionProvider dataProtectionProvider,
            IOptions<GoogleAuthSettings> options,
            IWebHostEnvironment environment)
        {
            _googleAuthService = googleAuthService;
            _googleOAuthClient = googleOAuthClient;
            _loginCodeStore = loginCodeStore;
            _stateProtector = dataProtectionProvider
                .CreateProtector("ColdChainX.GoogleOAuth.State.v1")
                .ToTimeLimitedDataProtector();
            _settings = options.Value;
            _environment = environment;
        }

        /// <summary>
        /// Sign in with a Google ID token obtained by Google Identity Services on the frontend.
        /// </summary>
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

        /// <summary>
        /// Start backend Google OAuth login.
        /// Open this endpoint URL directly in a browser, complete Google sign-in, then follow
        /// the redirects. Swagger's normal JSON request UI cannot complete this browser flow.
        /// </summary>
        [HttpGet("google-auth")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public IActionResult GoogleAuth()
        {
            if (!TryGetAbsoluteHttpUri(_settings.RedirectUri, out var redirectUri) ||
                string.IsNullOrWhiteSpace(_settings.ClientId))
            {
                return ConfigurationError(
                    "Google ClientId or RedirectUri is not configured correctly.");
            }

            var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var state = _stateProtector.Protect(nonce, StateLifetime);
            var stateHash = HashValue(nonce);
            var cookieName = BuildStateCookieName(nonce);

            Response.Cookies.Append(cookieName, stateHash, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = StateLifetime,
                Path = "/api/Accounts/google"
            });

            var authorizationUrl = QueryHelpers.AddQueryString(
                "https://accounts.google.com/o/oauth2/v2/auth",
                new Dictionary<string, string?>
                {
                    ["client_id"] = _settings.ClientId,
                    ["redirect_uri"] = redirectUri,
                    ["response_type"] = "code",
                    ["scope"] = "openid email profile",
                    ["state"] = state,
                    ["include_granted_scopes"] = "true",
                    ["prompt"] = "select_account"
                });

            return Redirect(authorizationUrl);
        }

        /// <summary>
        /// Google OAuth callback. This endpoint is called by Google, not directly by the frontend.
        /// </summary>
        [HttpGet("google/callback")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
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
                return UnauthorizedResponse("Invalid Google OAuth callback.");

            if (!TryValidateState(state))
                return UnauthorizedResponse("Invalid or expired Google OAuth state.");

            if (!TryGetAbsoluteHttpUri(_settings.RedirectUri, out var redirectUri))
                return ConfigurationError("Google RedirectUri is not configured correctly.");

            var idToken = await _googleOAuthClient.ExchangeCodeForIdTokenAsync(
                code,
                redirectUri,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(idToken))
                return UnauthorizedResponse("Google did not return a valid ID token.");

            var result = await _googleAuthService.AuthenticateAsync(idToken, cancellationToken);
            if (!result.Success || result.Data == null)
                return StatusCode(result.StatusCode, result);

            if (_environment.IsDevelopment() && _settings.BackendTestCallbackEnabled)
                return BuildDevelopmentResultPage(result.Data);

            if (!TryGetAbsoluteHttpUri(_settings.FrontendCallbackUrl, out var frontendCallbackUrl))
            {
                return ConfigurationError(
                    "Google FrontendCallbackUrl is not configured correctly.");
            }

            var oneTimeCode = WebEncoders.Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));
            await _loginCodeStore.StoreAsync(
                oneTimeCode,
                result.Data,
                LoginCodeLifetime,
                cancellationToken);

            var frontendUrl = QueryHelpers.AddQueryString(
                frontendCallbackUrl,
                "code",
                oneTimeCode);

            return Redirect(frontendUrl);
        }

        /// <summary>
        /// Exchange the short-lived, single-use Google login code for the application JWT.
        /// </summary>
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

            var authentication = await _loginCodeStore.TakeAsync(
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

        private bool TryValidateState(string protectedState)
        {
            string nonce;
            try
            {
                nonce = _stateProtector.Unprotect(protectedState);
            }
            catch (CryptographicException)
            {
                return false;
            }

            var cookieName = BuildStateCookieName(nonce);
            if (!Request.Cookies.TryGetValue(cookieName, out var cookieValue))
                return false;

            Response.Cookies.Delete(cookieName, new CookieOptions
            {
                Path = "/api/Accounts/google"
            });

            var expectedBytes = Encoding.UTF8.GetBytes(HashValue(nonce));
            var actualBytes = Encoding.UTF8.GetBytes(cookieValue);
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }

        private IActionResult BuildDevelopmentResultPage(GoogleLoginResponse authentication)
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

        private ObjectResult ConfigurationError(string message)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Failure(
                    message,
                    StatusCodes.Status500InternalServerError));
        }

        private UnauthorizedObjectResult UnauthorizedResponse(string message)
        {
            return Unauthorized(ApiResponse<object>.Failure(
                message,
                StatusCodes.Status401Unauthorized));
        }

        private static string BuildStateCookieName(string nonce)
            => StateCookiePrefix + HashValue(nonce)[..16];

        private static string HashValue(string value)
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
