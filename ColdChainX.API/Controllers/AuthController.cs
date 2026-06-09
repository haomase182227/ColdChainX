using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
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

        // Admin endpoint: create a customer user and customer record with role assigned
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPost("create-customer")]
        public async Task<IActionResult> CreateCustomer([FromBody] RegisterRequest request)
        {
            request.Role = "Customer";
            var result = await _authService.RegisterAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        // Admin endpoint: create a driver user and driver record with role assigned
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPost("create-driver")]
        public async Task<IActionResult> CreateDriver([FromBody] RegisterRequest request)
        {
            request.Role = "Driver";
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

        /// <summary>
        /// Update profile for the current user.
        /// </summary>
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

        /// <summary>
        /// Soft delete a user by id.
        /// </summary>
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> SoftDeleteUser(Guid id)
        {
            var result = await _authService.SoftDeleteUserAsync(id);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
