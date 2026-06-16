using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DispatchController : ControllerBase
{
    private readonly IDispatchService _dispatchService;

    public DispatchController(IDispatchService dispatchService)
    {
        _dispatchService = dispatchService;
    }

    /// <summary>
    /// Lập kế hoạch lấy hàng từ kho và ghép chuyến.
    ///
    /// Workflow:
    /// 1. Validate hàng IN_WAREHOUSE + kiểm tra tải trọng/CBM xe
    /// 2. Tính lộ trình tối ưu qua các điểm giao (Goong API + Nearest Neighbor TSP)
    /// 3. Gợi ý xếp hàng theo thuật toán LIFO nội bộ (điểm giao cuối → xếp trước)
    /// 4. Tạo MasterTrip + TripStops
    /// 5. Cập nhật trạng thái đơn hàng → LOADING (sinh lệnh điều động)
    /// 6. Gửi thông báo cho Điều phối viên (Dispatcher)
    /// </summary>
    [HttpPost("plan-load")]
    [ProducesResponseType(typeof(PlanLoadResult), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(typeof(object), 500)]
    public async Task<IActionResult> PlanLoad([FromBody] PlanLoadRequest request)
    {
        try
        {
            var result = await _dispatchService.PlanLoadFromWarehouseAsync(request);
            return Ok(new
            {
                Success = true,
                Message = $"Đã lập kế hoạch chuyến hàng thành công. " +
                          $"Trip: {result.TripId}, " +
                          $"Lộ trình: {result.Route.TotalStops} điểm dừng / {result.Route.TotalDistanceKm}km, " +
                          $"Đã thông báo {result.NotifiedCoordinators} điều phối viên.",
                Data = result
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống.", Details = ex.Message });
        }
    }

    // ── Legacy endpoints ───────────────────────────────────────────────────

    /// <summary>[Legacy] Gợi ý xếp hàng bằng Gemini AI (không dùng Goong, không tạo Trip).</summary>
    [HttpPost("suggest-load")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(object), 400)]
    public async Task<IActionResult> SuggestLoad([FromBody] SuggestLoadRequest request)
    {
        try
        {
            var plan = await _dispatchService.SuggestLoadPlanAsync(request.OrderIds, request.VehicleId);
            return Ok(new { Success = true, Plan = plan });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Internal server error.", Details = ex.Message });
        }
    }

    /// <summary>[Legacy] Tính route và LIFO cho Trip đã tạo sẵn.</summary>
    [HttpPost("route-lifo/{tripId}")]
    public async Task<IActionResult> CalculateRouteAndLIFO(Guid tripId)
    {
        try
        {
            await _dispatchService.CalculateRouteAndLIFOAsync(tripId);
            return Ok(new { Success = true, Message = "Route calculated and LIFO stops planned." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>Đóng hàng và kẹp chì niêm phong.</summary>
    [HttpPost("seal/{tripId}")]
    public async Task<IActionResult> SealTruck(Guid tripId, [FromBody] SealRequest request)
    {
        try
        {
            // Lấy userId từ JWT claim
            var keeperIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var keeperId = keeperIdClaim != null ? Guid.Parse(keeperIdClaim) : Guid.NewGuid();

            await _dispatchService.SealTruckAsync(tripId, request.SealCode, keeperId);
            return Ok(new { Success = true, Message = "Truck sealed successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>Cấp giấy đi đường / E-Waybill cho chuyến.</summary>
    [HttpPost("issue-documents/{tripId}")]
    public async Task<IActionResult> IssueDocuments(Guid tripId)
    {
        try
        {
            await _dispatchService.IssueDispatchDocumentsAsync(tripId);
            return Ok(new { Success = true, Message = "Dispatch documents issued." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>[Test Only] Test kết nối Gemini API.</summary>
    [AllowAnonymous]
    [HttpGet("test-gemini")]
    public async Task<IActionResult> TestGemini(
        [FromServices] ColdChainX.Infrastructure.Integration.GeminiLoadOptimizerClient geminiClient)
    {
        try
        {
            var mockVehicle = new ColdChainX.Core.Entities.Vehicle
            {
                VehicleId = Guid.NewGuid(),
                MaxCbm    = 10.26m,
                MaxWeight = 5000m
            };

            var mockOrders = new List<ColdChainX.Core.Entities.TransportOrder>
            {
                new()
                {
                    OrderId          = Guid.NewGuid(),
                    ItemName         = "Frozen Fish",
                    Quantity         = 50,
                    ExpectedCbm      = 2.5m,
                    ExpectedWeightKg = 1000m,
                    DestLocation     = Guid.NewGuid()
                },
                new()
                {
                    OrderId          = Guid.NewGuid(),
                    ItemName         = "Ice Cream",
                    Quantity         = 100,
                    ExpectedCbm      = 1.0m,
                    ExpectedWeightKg = 500m,
                    DestLocation     = Guid.NewGuid()
                }
            };

            var routeSequence = new List<Guid>
            {
                mockOrders[1].DestLocation!.Value,
                mockOrders[0].DestLocation!.Value
            };

            var plan = await geminiClient.OptimizeLoadPlanAsync(mockVehicle, mockOrders, routeSequence);
            return Ok(new { Message = "Gemini API Test Success", Plan = plan });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}

// ── Request/Response models ──────────────────────────────────────────────────

/// <summary>Request cho endpoint legacy suggest-load (chỉ dùng Gemini).</summary>
public class SuggestLoadRequest
{
    public List<Guid> OrderIds { get; set; } = new();
    public Guid VehicleId { get; set; }
}

public class SealRequest
{
    public string SealCode { get; set; } = null!;
}
