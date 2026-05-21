using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var result = await _authService.RegisterAsync(request);
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

        [HttpPost("refresh-tokens")]
        public async Task<IActionResult> Refresh([FromBody] string refreshToken)
        {
            var result = await _authService.RefreshTokensAsync(refreshToken);
            if (!result.Success) return Unauthorized(result);
            return Ok(result);
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
        {
            var result = await _authService.LogoutAsync(request.UserId);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
