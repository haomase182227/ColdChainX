using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Constants;
using SecurityClaim = System.Security.Claims.Claim;

namespace ColdChainX.Infrastructure.Services
{
    public class JwtService : IJwtService
    {
        private readonly JwtSettings _settings;

        public JwtService(IOptions<JwtSettings> options)
        {
            _settings = options.Value;
        }

        public string GenerateAccessToken(User user, DateTime expiresAt)
        {
            var keyBytes = Encoding.UTF8.GetBytes(_settings.Key);
            var securityKey = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var roleName = user.Role?.RoleName;

            var claims = new List<SecurityClaim>
            {
                new SecurityClaim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new SecurityClaim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new SecurityClaim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new SecurityClaim("username", user.Username),
                new SecurityClaim("name", user.FullName),
                new SecurityClaim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            if (!string.IsNullOrWhiteSpace(roleName))
            {
                claims.Add(new SecurityClaim(ClaimTypes.Role, roleName));

                var normalizedRoleName = NormalizeRoleName(roleName);
                if (!string.Equals(roleName, normalizedRoleName, StringComparison.Ordinal))
                    claims.Add(new SecurityClaim(ClaimTypes.Role, normalizedRoleName));
            }

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Guid.NewGuid().ToString();
        }

        private static string NormalizeRoleName(string roleName)
            => roleName.ToUpperInvariant() switch
            {
                "ADMIN" => "Admin",
                "MANAGER" => "Manager",
                "STAFF" => "Staff",
                "DRIVER" => "Driver",
                _ => roleName
            };
    }
}
