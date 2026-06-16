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
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Register([FromForm] RegisterRequest request)
        {
            var result = await _authService.RegisterAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        // Get all available roles for registration
        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            var result = await _authService.GetAllRolesAsync();
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        // Public endpoint for customer self-registration
        [AllowAnonymous]
        [HttpPost("create-customer")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateCustomer([FromForm] CreateCustomerRequest request)
        {
            var result = await _authService.CreateCustomerAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        // Admin endpoint: create a driver user and driver record with role assigned
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPost("create-driver")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateDriver([FromForm] CreateDriverRequest request)
        {
            var result = await _authService.CreateDriverAsync(request);
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
        /// Admin: update driver's full name.
        /// </summary>
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPatch("update-driver/{driverId:guid}/fullname")]
        public async Task<IActionResult> UpdateDriverFullName(Guid driverId, [FromBody] UpdateDriverFullNameRequest request)
        {
            var mapped = new UpdateDriverInfoRequest { FullName = request.FullName };
            var result = await _authService.UpdateDriverAsync(driverId, mapped);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Admin: update driver's password.
        /// </summary>
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPatch("update-driver/{driverId:guid}/password")]
        public async Task<IActionResult> UpdateDriverPassword(Guid driverId, [FromBody] UpdateDriverPasswordRequest request)
        {
            var mapped = new UpdateDriverInfoRequest { NewPassword = request.NewPassword };
            var result = await _authService.UpdateDriverAsync(driverId, mapped);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Admin: update driver's date of birth.
        /// </summary>
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPatch("update-driver/{driverId:guid}/date-of-birth")]
        public async Task<IActionResult> UpdateDriverDob(Guid driverId, [FromBody] UpdateDriverDobRequest request)
        {
            var mapped = new UpdateDriverInfoRequest { DateOfBirth = request.DateOfBirth };
            var result = await _authService.UpdateDriverAsync(driverId, mapped);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Admin: update driver's operational status (AVAILABLE, ON_TRIP, OFFLINE, INACTIVE).
        /// </summary>
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPatch("update-driver/{driverId:guid}/status")]
        public async Task<IActionResult> UpdateDriverStatus(Guid driverId, [FromBody] UpdateDriverStatusRequest request)
        {
            var mapped = new UpdateDriverInfoRequest { Status = request.Status };
            var result = await _authService.UpdateDriverAsync(driverId, mapped);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Admin: update or create driver license information.
        /// </summary>
        [Authorize(Roles = "Admin,ADMIN")]
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
