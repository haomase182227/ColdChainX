using ColdChainX.Application.DTOs.GoogleAuth;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Constants;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColdChainX.Infrastructure.Services
{
    public class GoogleIdTokenValidator : IGoogleIdTokenValidator
    {
        private readonly GoogleAuthSettings _settings;
        private readonly ILogger<GoogleIdTokenValidator> _logger;

        public GoogleIdTokenValidator(
            IOptions<GoogleAuthSettings> options,
            ILogger<GoogleIdTokenValidator> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }

        public async Task<VerifiedGoogleUserDto?> ValidateAsync(
            string idToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId))
                throw new InvalidOperationException("Authentication:Google:ClientId is not configured.");

            var validationSettings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _settings.ClientId }
            };

            try
            {
                var payload = await GoogleJsonWebSignature.ValidateAsync(
                    idToken,
                    validationSettings);

                return new VerifiedGoogleUserDto
                {
                    GoogleId = payload.Subject,
                    Email = payload.Email,
                    Name = payload.Name,
                    Picture = payload.Picture,
                    EmailVerified = payload.EmailVerified
                };
            }
            catch (Exception exception)
                when (exception is InvalidJwtException or ArgumentException or FormatException)
            {
                _logger.LogInformation(
                    "Google ID token validation failed with {ExceptionType}.",
                    exception.GetType().Name);
                return null;
            }
        }
    }
}
