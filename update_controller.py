import re

with open('ColdChainX.API/Controllers/DispatchController.cs', 'r') as f:
    content = f.read()

# 1. Add GetCurrentUserId
if 'GetCurrentUserId' not in content:
    content = content.replace(
        '_db = db;\n    }',
        '_db = db;\n    }\n\n    private Guid GetCurrentUserId()\n    {\n        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;\n        return Guid.TryParse(idClaim, out var userId) ? userId : Guid.Empty;\n    }'
    )

# 2. Find the start of plan-load
start_idx = content.find('    /// <summary>\n    /// Lập kế hoạch lấy hàng từ kho và ghép chuyến.')
if start_idx != -1:
    content = content[:start_idx]

# 3. Append the new APIs
new_apis = """
    // ═══════════════════════════════════════════════════════════════════════
    //  API 1: AUTO-DISPATCH — Tự động ghép chuyến
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tự động quét đơn hàng IN_WAREHOUSE (ưu tiên lâu nhất), nhóm theo nhiệt độ + điểm đến,
    /// chọn xe/tài xế (giấy tờ còn hạn), tính lộ trình TSP + Goong, sinh LIFO, sinh navigation.
    /// </summary>
    [HttpPost("auto-dispatch")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AutoDispatchResult), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(typeof(object), 500)]
    public async Task<IActionResult> AutoDispatch([FromForm] AutoDispatchFormRequest form)
    {
        var rawOriginLocId = ExtractGuid(form.OriginWarehouseLocationId);
        if (!Guid.TryParse(rawOriginLocId, out var originLocId))
            return BadRequest(new { Success = false, Error = "OriginWarehouseLocationId không hợp lệ." });

        if (form.PlannedStartTime >= form.PlannedEndTime)
            return BadRequest(new { Success = false, Error = "PlannedStartTime phải nhỏ hơn PlannedEndTime." });

        var request = new AutoDispatchRequest
        {
            OriginWarehouseLocationId = originLocId,
            PlannedStartTime          = form.PlannedStartTime,
            PlannedEndTime            = form.PlannedEndTime,
            TempConditionFilter       = form.TempConditionFilter,
            MaxOrdersPerTrip          = form.MaxOrdersPerTrip > 0 ? form.MaxOrdersPerTrip : 50
        };

        try
        {
            var result = await _dispatchService.AutoDispatchAsync(request);
            return Ok(new { Success = true, Data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống khi auto-dispatch.", Detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 2: WAREHOUSE ORDER — Lệnh bốc xếp cho kho
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tạo lệnh bốc xếp cho kho (sau khi đã dispatch). Trip chuyển sang PENDING_WH_APPROVAL.
    /// Gửi thông báo cho WarehouseMonitor để duyệt.
    /// </summary>
    [HttpPost("warehouse-order/{tripId}")]
    [ProducesResponseType(typeof(WarehouseOrderResult), 200)]
    public async Task<IActionResult> CreateWarehouseOrder(Guid tripId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _dispatchService.CreateWarehouseOrderAsync(tripId, currentUserId);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// WH Monitor duyệt lệnh bốc xếp. Trip và Orders chuyển sang LOADING.
    /// Gửi thông báo cho Loader.
    /// </summary>
    [HttpPost("warehouse-order/{tripId}/approve")]
    [ProducesResponseType(typeof(WarehouseOrderResult), 200)]
    public async Task<IActionResult> ApproveWarehouseOrder(Guid tripId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _dispatchService.ApproveWarehouseOrderAsync(tripId, currentUserId);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// WH Monitor từ chối lệnh bốc xếp. Trip chuyển sang WH_REJECTED, Orders về IN_WAREHOUSE.
    /// </summary>
    [HttpPost("warehouse-order/{tripId}/reject")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(WarehouseOrderResult), 200)]
    public async Task<IActionResult> RejectWarehouseOrder(Guid tripId, [FromForm] RejectWarehouseOrderRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { Success = false, Error = "Vui lòng nhập lý do từ chối." });

        try
        {
            var result = await _dispatchService.RejectWarehouseOrderAsync(tripId, currentUserId, request.Reason);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 3: IOT CHECK — Kiểm tra tín hiệu IoT xe
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kiểm tra trạng thái kết nối, GPS, nhiệt độ, pin của các thiết bị IoT gắn trên xe.
    /// </summary>
    [HttpGet("vehicle-iot-check/{vehicleId}")]
    [ProducesResponseType(typeof(VehicleIoTStatus), 200)]
    public async Task<IActionResult> CheckVehicleIoT(Guid vehicleId)
    {
        try
        {
            var result = await _dispatchService.CheckVehicleIoTAsync(vehicleId);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API 4: SEAL & DISPATCH — Kẹp chì + kiểm tra chất hàng
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kiểm tra tất cả đơn hàng đã được xếp lên xe chưa. Nếu đủ → kẹp chì → cấp E-Waybill.
    /// Chuyển Trip sang SEALED / DISPATCHED.
    /// </summary>
    [HttpPost("seal-and-dispatch/{tripId}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SealAndDispatchResult), 200)]
    public async Task<IActionResult> SealAndDispatch(Guid tripId, [FromForm] SealAndDispatchRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SealCode))
            return BadRequest(new { Success = false, Error = "SealCode là bắt buộc." });

        try
        {
            var result = await _dispatchService.SealAndDispatchAsync(tripId, request.SealCode, currentUserId);
            return Ok(new { Success = true, Data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Error = "Lỗi hệ thống khi kẹp chì.", Detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BACKLOG — Xử lý hàng tồn
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Quét các đơn hàng IN_WAREHOUSE tồn lâu hơn số ngày chỉ định, ghép vào các xe nhỏ (≤ 2000kg).
    /// </summary>
    [HttpPost("process-backlog")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(BacklogDispatchResult), 200)]
    public async Task<IActionResult> ProcessBacklog([FromForm] ProcessBacklogRequest request)
    {
        var rawOriginLocId = ExtractGuid(request.OriginWarehouseLocationId);
        if (!Guid.TryParse(rawOriginLocId, out var originLocId))
            return BadRequest(new { Success = false, Error = "OriginWarehouseLocationId không hợp lệ." });

        if (request.PlannedStartTime >= request.PlannedEndTime)
            return BadRequest(new { Success = false, Error = "PlannedStartTime phải nhỏ hơn PlannedEndTime." });

        var backlogDays = request.BacklogDays > 0 ? request.BacklogDays : 1;

        try
        {
            var result = await _dispatchService.ProcessBacklogOrdersAsync(
                originLocId, request.PlannedStartTime, request.PlannedEndTime, backlogDays);
            return Ok(new { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Success = false, Error = ex.Message });
        }
    }

    private static string ExtractGuid(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var parts = input.Split(new[] { ':', '|' });
        return parts[0].Trim();
    }
}
"""

content += new_apis

with open('ColdChainX.API/Controllers/DispatchController.cs', 'w') as f:
    f.write(content)
