using ColdChainX.Application.Features.Outbound.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OutboundController : ControllerBase
{
    private readonly IMediator _mediator;

    public OutboundController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// [STEP 3/5] Xac nhan mot LPN da duoc boc len xe.
    /// </summary>
    /// <remarks>
    /// LPN state: LOADING → LOADING_COMPLETED
    ///
    /// Precondition:
    ///   - LPN.State == LOADING  (set boi POST /api/Dispatch/trip/{id}/start-picking)
    ///   - Trip.Status == PICKING
    ///
    /// Postcondition:
    ///   - LPN.State = LOADING_COMPLETED
    ///
    /// Goi API nay tung LPN mot lan cho den khi tat ca LPN trong chuyen
    /// deu o trang thai LOADING_COMPLETED.
    ///
    /// Next step: POST /api/Outbound/load-trip (khi tat ca LPN da LOADING_COMPLETED)
    /// </remarks>
    [HttpPost("pick")]
    [ProducesResponseType(typeof(PickLpnResponse), 200)]
    [ProducesResponseType(typeof(PickLpnResponse), 400)]
    public async Task<IActionResult> Pick([FromBody] PickLpnCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// [STEP 4/5] Xac nhan toan bo chuyen da len xe — chuyen LPN tu LOADING_COMPLETED sang RELEASED.
    /// </summary>
    /// <remarks>
    /// LPN state: LOADING_COMPLETED → RELEASED
    /// Trip status: PICKING → LOADING_COMPLETED
    ///
    /// Precondition:
    ///   - TAT CA LPN cua TripId phai o trang thai LOADING_COMPLETED
    ///     (moi LPN da duoc goi POST /api/Outbound/pick truoc do)
    ///
    /// Postcondition:
    ///   - Tat ca LPN.State = RELEASED
    ///   - Trip.Status = LOADING_COMPLETED
    ///   - Sinh ManifestPdf + OutboundTicketPdf (neu co)
    ///
    /// Next step: POST /api/Dispatch/seal-and-dispatch/{tripId}
    /// </remarks>
    [HttpPost("load-trip")]
    [ProducesResponseType(typeof(CompleteTripLoadingResponse), 200)]
    [ProducesResponseType(typeof(CompleteTripLoadingResponse), 400)]
    public async Task<IActionResult> LoadTrip([FromBody] CompleteTripLoadingCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// [HELPER cho STEP 3] Lay danh sach LPN dang o trang thai LOADING — san sang de goi POST /api/Outbound/pick.
    /// </summary>
    /// <remarks>
    /// Tra ve cac LPN co State == LOADING (da duoc start-picking).
    /// Dung LpnId trong ket qua lam body cho POST /api/Outbound/pick.
    ///
    /// Tham so tuy chon: ?tripId={guid} de chi lay LPN cua mot chuyen cu the.
    /// </remarks>
    [HttpGet("available-lpns")]
    [ProducesResponseType(typeof(List<ColdChainX.Application.Features.Outbound.DTOs.AvailableLpnDto>), 200)]
    public async Task<IActionResult> GetAvailableLpns([FromQuery] Guid? tripId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetAvailableLpnsQuery(tripId));
        return Ok(result);
    }

    /// <summary>
    /// [HELPER cho STEP 4] Lay danh sach chuyen dang PICKING kem tat ca LPN/don hang ben trong.
    /// </summary>
    /// <remarks>
    /// Tra ve cac chuyen co Status == PICKING, moi chuyen kem danh sach LPN
    /// (LOADING + LOADING_COMPLETED) va co ReadyToLoad = true khi tat ca LPN da LOADING_COMPLETED.
    ///
    /// Dung TripId trong ket qua lam body cho POST /api/Outbound/load-trip
    /// (chi goi load-trip khi ReadyToLoad == true).
    ///
    /// Tham so tuy chon: ?tripId={guid} de xem chi tiet mot chuyen.
    /// </remarks>
    [HttpGet("available-trips")]
    [ProducesResponseType(typeof(List<ColdChainX.Application.Features.Outbound.DTOs.AvailableTripDto>), 200)]
    public async Task<IActionResult> GetAvailableTrips([FromQuery] Guid? tripId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetAvailableTripsQuery(tripId));
        return Ok(result);
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders()
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetOutboundOrdersQuery());
        return Ok(result);
    }

    [HttpGet("pick-list/{masterTripId}")]
    public async Task<IActionResult> GetPickList(Guid masterTripId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetOutboundPickListQuery(masterTripId));
        return Ok(result);
    }

    [HttpGet("orders/{orderId}/epod-pdf")]
    public async Task<IActionResult> GetEpodPdf(Guid orderId)
    {
        try
        {
            var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GenerateEpodPdfQuery(orderId));
            return File(pdfBytes, "application/pdf", $"ePOD_{orderId.ToString().Substring(0, 8)}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}
