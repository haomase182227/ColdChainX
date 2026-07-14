using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/users")]
    [Authorize(Policy = "AdminOnly")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Retrieve a paginated, filtered, and sorted list of users.
        /// </summary>
        /// <param name="page">1-based page index.</param>
        /// <param name="pageSize">Number of items per page.</param>
        /// <param name="search">Optional term to search by username, email, or full name.</param>
        /// <param name="role">Optional filter by user role.</param>
        /// <param name="status">Optional filter by user status.</param>
        /// <param name="sortBy">Field to sort by (username, email, fullname, status, role, createdat).</param>
        /// <param name="order">Sort order (asc or desc).</param>
        [HttpGet]
        public async Task<IActionResult> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? role = null,
            [FromQuery] UserStatus? status = null,
            [FromQuery] string? sortBy = "createdat",
            [FromQuery] string? order = "asc")
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            var result = await _userService.GetUsersAsync(page, pageSize, search, role, status, sortBy, order);
            return Ok(result);
        }

        /// <summary>
        /// Get user profile details by ID.
        /// </summary>
        /// <param name="id">The user's unique identifier.</param>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            var result = await _userService.GetUserByIdAsync(id);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        /// <summary>
        /// Create a new user account (Admin only).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            var result = await _userService.CreateUserAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Update an existing user's details (Admin only).
        /// </summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] AdminUpdateUserRequest request)
        {
            var result = await _userService.UpdateUserAsync(id, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Change a user's role (Admin only).
        /// </summary>
        [HttpPatch("{id:guid}/role")]
        public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request)
        {
            var result = await _userService.ChangeRoleAsync(id, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Change a user's status (Admin only).
        /// </summary>
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeUserStatusRequest request)
        {
            var result = await _userService.ChangeStatusAsync(id, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Change a warehouse operator's assigned warehouse (Admin only).
        /// </summary>
        [HttpPatch("{id:guid}/warehouse")]
        public async Task<IActionResult> UpdateWarehouse(Guid id, [FromBody] UpdateUserWarehouseRequest request)
        {
            var result = await _userService.UpdateWarehouseAsync(id, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Reset a user's password (Admin only).
        /// </summary>
        [HttpPost("{id:guid}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
        {
            var result = await _userService.ResetPasswordAsync(id, request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Restore a soft-deleted user (Admin only).
        /// </summary>
        [HttpPost("{id:guid}/restore")]
        public async Task<IActionResult> Restore(Guid id)
        {
            var result = await _userService.RestoreAsync(id);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// Soft delete a user account (Admin only).
        /// </summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var result = await _userService.SoftDeleteAsync(id);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
    }
}
