using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

public class WarehouseFlowService : IWarehouseFlowService
{
    private const decimal DiscrepancyThresholdPercent = 5m;
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<NotificationHub> _hubContext;

    public WarehouseFlowService(ApplicationDbContext db, IHubContext<NotificationHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
    }

    public async Task<ApiResponse<LpnResponse>> ProcessInboundQcAsync(Guid orderId, ProcessInboundQcRequest request, Guid receiverId)
    {
        var order = await _db.TransportOrders
            .Include(o => o.Route)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

        if (order == null)
            return ApiResponse<LpnResponse>.Failure("Order not found.");

        if (request.ActualWeightKg <= 0 || request.LengthCm <= 0 || request.WidthCm <= 0 || request.HeightCm <= 0)
            return ApiResponse<LpnResponse>.Failure("Actual weight and dimensions must be greater than 0.");

        var actualCbm = CalculateCbm(request.LengthCm, request.WidthCm, request.HeightCm, order.Quantity);
        var weightDiff = CalculateDiffPercent(order.ExpectedWeightKg, request.ActualWeightKg);
        var cbmDiff = CalculateDiffPercent(order.ExpectedCbm, actualCbm);
        var maxDiff = Math.Max(weightDiff, cbmDiff);
        var hasDiscrepancy = maxDiff > DiscrepancyThresholdPercent;
        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync();

        var lpn = new Lpn
        {
            LpnId = Guid.NewGuid(),
            LpnCode = GenerateCode("LPN"),
            OrderId = order.OrderId,
            Order = order,
            CustomerId = order.CustomerId,
            RouteId = order.RouteId,
            TripId = order.MasterTripId,
            Quantity = order.Quantity,
            ActualWeightKg = request.ActualWeightKg,
            ActualCbm = actualCbm,
            RequiredTemperature = ParseTemperature(order.TempCondition),
            RecordedTemperature = request.RecordedTemperature,
            State = hasDiscrepancy ? LpnState.DISCREPANCY_HOLD : LpnState.RECEIVING,
            DiscrepancyReason = hasDiscrepancy
                ? $"Actual cargo differs from expected by {maxDiff:0.##}% (weight {weightDiff:0.##}%, cbm {cbmDiff:0.##}%)."
                : null,
            SlaDeadline = CalculateSlaDeadline(order.Route),
            CreatedAt = now
        };

        _db.Lpns.Add(lpn);
        order.ActualWeightKg = request.ActualWeightKg;
        order.ActualCbm = actualCbm;
        order.Status = hasDiscrepancy ? "DISCREPANCY_HOLD" : "RECEIVING";

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        if (hasDiscrepancy)
        {
            await _hubContext.Clients.Group("Group_Sales").SendAsync("InboundDiscrepancyDetected", new
            {
                lpn.LpnId,
                lpn.LpnCode,
                order.OrderId,
                order.TrackingCode,
                MaxDiffPercent = maxDiff,
                lpn.DiscrepancyReason
            });
        }

        var message = hasDiscrepancy
            ? "Inbound QC completed with discrepancy hold."
            : "Inbound QC completed and GRN generated.";

        return ApiResponse<LpnResponse>.SuccessResponse(ToResponse(lpn, order.TrackingCode), message);
    }

    public async Task<ApiResponse<object>> ResolveDiscrepancyAsync(Guid lpnId, ResolveDiscrepancyRequest request, Guid salesUserId)
    {
        var lpn = await _db.Lpns.Include(x => x.Order).FirstOrDefaultAsync(x => x.LpnId == lpnId);
        if (lpn == null)
            return ApiResponse<object>.Failure("LPN not found.");

        if (lpn.State != LpnState.DISCREPANCY_HOLD)
            return ApiResponse<object>.Failure("Only LPNs in DISCREPANCY_HOLD can be resolved.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        if (request.AcceptSurcharge)
        {
            lpn.State = LpnState.RECEIVING;
            lpn.UpdatedAt = DateTime.UtcNow;
            lpn.Order.Status = "RECEIVING";

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return ApiResponse<object>.SuccessResponse(ToResponse(lpn, lpn.Order.TrackingCode), "Discrepancy accepted. LPN moved to RECEIVING and waiting putaway.");
        }

        var handlingFee = request.HandlingFee ?? 150000m;
        var storageFee = request.StorageFee ?? 50000m;
        var bill = new PenaltyBill
        {
            PenaltyBillId = Guid.NewGuid(),
            BillCode = GenerateCode("PEN"),
            LpnId = lpn.LpnId,
            OrderId = lpn.OrderId,
            CustomerId = lpn.CustomerId,
            HandlingFee = handlingFee,
            StorageFee = storageFee,
            TotalAmount = handlingFee + storageFee,
            Reason = request.Note ?? "Customer rejected discrepancy surcharge.",
            IsPaid = false,
            CreatedAt = DateTime.UtcNow
        };

        lpn.State = LpnState.RETURN_PENDING;
        lpn.UpdatedAt = DateTime.UtcNow;
        lpn.Order.Status = "RETURN_PENDING";
        _db.PenaltyBills.Add(bill);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return ApiResponse<object>.SuccessResponse(ToPenaltyResponse(bill), "Customer rejected surcharge. Penalty bill created.");
    }

    public async Task<ApiResponse<LpnResponse>> PutawayLpnAsync(Guid lpnId, PutawayLpnRequest request)
    {
        var lpn = await _db.Lpns.Include(x => x.Order).FirstOrDefaultAsync(x => x.LpnId == lpnId);
        if (lpn == null)
            return ApiResponse<LpnResponse>.Failure("LPN not found.");

        if (lpn.State != LpnState.RECEIVING && lpn.State != LpnState.IN_STOCK)
            return ApiResponse<LpnResponse>.Failure("Only RECEIVING or IN_STOCK LPNs can be put away.");

        if (string.IsNullOrWhiteSpace(request.StorageLocation))
            return ApiResponse<LpnResponse>.Failure("StorageLocation is required.");

        lpn.StorageLocation = request.StorageLocation.Trim();
        lpn.InboundTime ??= DateTime.UtcNow;
        lpn.State = LpnState.IN_STOCK;
        lpn.UpdatedAt = DateTime.UtcNow;
        lpn.Order.Status = "IN_STOCK";

        await _db.SaveChangesAsync();

        return ApiResponse<LpnResponse>.SuccessResponse(ToResponse(lpn, lpn.Order.TrackingCode), "LPN putaway completed.");
    }

    public async Task<ApiResponse<List<LpnResponse>>> GetInventoryAgingAsync(string? state, string? storageLocation)
    {
        var query = _db.Lpns.Include(x => x.Order).AsQueryable();

        if (!string.IsNullOrWhiteSpace(state) && Enum.TryParse<LpnState>(state, true, out var parsedState))
            query = query.Where(x => x.State == parsedState);

        if (!string.IsNullOrWhiteSpace(storageLocation))
            query = query.Where(x => x.StorageLocation != null && EF.Functions.ILike(x.StorageLocation, $"%{storageLocation}%"));

        var lpns = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .ToListAsync();

        var result = lpns.Select(x => ToResponse(x, x.Order.TrackingCode)).ToList();
        return ApiResponse<List<LpnResponse>>.SuccessResponse(result, "Inventory aging retrieved.");
    }

    public async Task<ApiResponse<LpnResponse>> PickLpnAsync(Guid lpnId)
    {
        var lpn = await _db.Lpns.Include(x => x.Order).FirstOrDefaultAsync(x => x.LpnId == lpnId);
        if (lpn == null)
            return ApiResponse<LpnResponse>.Failure("LPN not found.");

        if (lpn.State != LpnState.IN_STOCK && lpn.State != LpnState.ALLOCATED)
            return ApiResponse<LpnResponse>.Failure("Only IN_STOCK or ALLOCATED LPNs can be picked.");

        lpn.State = LpnState.LOADING;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ApiResponse<LpnResponse>.SuccessResponse(ToResponse(lpn, lpn.Order.TrackingCode), "LPN picked.");
    }

    public async Task<ApiResponse<TripLoadingResponse>> CompleteTripLoadingAsync(Guid tripId, CompleteTripLoadingRequest request)
    {
        if (request.LpnIds.Count == 0)
            return ApiResponse<TripLoadingResponse>.Failure("At least one LPN is required.");

        if (string.IsNullOrWhiteSpace(request.SealNumber))
            return ApiResponse<TripLoadingResponse>.Failure("SealNumber is required.");

        var trip = await _db.MasterTrips.FirstOrDefaultAsync(x => x.TripId == tripId);
        if (trip == null)
            return ApiResponse<TripLoadingResponse>.Failure("Trip not found.");

        var lpns = await _db.Lpns.Where(x => request.LpnIds.Contains(x.LpnId)).ToListAsync();
        if (lpns.Count != request.LpnIds.Count)
            return ApiResponse<TripLoadingResponse>.Failure("One or more LPNs were not found.");

        if (lpns.Any(x => x.State != LpnState.LOADING))
            return ApiResponse<TripLoadingResponse>.Failure("All LPNs must be in LOADING state before loading completion.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var now = DateTime.UtcNow;
        foreach (var lpn in lpns)
        {
            lpn.TripId = tripId;
            lpn.State = LpnState.RELEASED;
            lpn.UpdatedAt = now;
        }

        _db.Seals.Add(new Seal
        {
            SealId = Guid.NewGuid(),
            TripId = tripId,
            SealCode = request.SealNumber.Trim(),
            AppliedAt = now,
            Status = "ACTIVE",
            CreatedAt = now,
            Note = "Created by outbound loading completion."
        });

        trip.Status = "LOADED";

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var response = new TripLoadingResponse
        {
            TripId = tripId,
            SealNumber = request.SealNumber.Trim(),
            ShippedLpnCount = lpns.Count,
            HandoverPdfUrl = MockPdfUrl("handover", tripId.ToString("N")),
            InternalIssuePdfUrl = MockPdfUrl("internal-issue", tripId.ToString("N")),
            ManifestPdfUrl = MockPdfUrl("manifest", tripId.ToString("N"))
        };

        return ApiResponse<TripLoadingResponse>.SuccessResponse(response, "Trip loading completed.");
    }

    public async Task<ApiResponse<PenaltyBillResponse>> MarkPenaltyBillPaidAsync(Guid penaltyBillId, Guid accountantUserId)
    {
        var bill = await _db.PenaltyBills.FirstOrDefaultAsync(x => x.PenaltyBillId == penaltyBillId);
        if (bill == null)
            return ApiResponse<PenaltyBillResponse>.Failure("Penalty bill not found.");

        bill.IsPaid = true;
        bill.PaidAt = DateTime.UtcNow;
        bill.PaidBy = accountantUserId;

        await _db.SaveChangesAsync();

        return ApiResponse<PenaltyBillResponse>.SuccessResponse(ToPenaltyResponse(bill), "Penalty bill marked as paid.");
    }

    public async Task<ApiResponse<LpnResponse>> GenerateReturnPdfAsync(Guid lpnId)
    {
        var lpn = await _db.Lpns
            .Include(x => x.Order)
            .Include(x => x.PenaltyBills)
            .FirstOrDefaultAsync(x => x.LpnId == lpnId);

        if (lpn == null)
            return ApiResponse<LpnResponse>.Failure("LPN not found.");

        if (lpn.State != LpnState.RETURN_PENDING)
            return ApiResponse<LpnResponse>.Failure("Only RETURN_PENDING LPNs can generate return PDF.");

        if (!lpn.PenaltyBills.Any(x => x.IsPaid))
            return ApiResponse<LpnResponse>.Failure("Penalty bill must be paid before generating return PDF.");

        lpn.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ApiResponse<LpnResponse>.SuccessResponse(ToResponse(lpn, lpn.Order.TrackingCode), "Return PDF generated.");
    }

    private static decimal CalculateCbm(decimal lengthCm, decimal widthCm, decimal heightCm, int quantity)
    {
        return Math.Round(lengthCm * widthCm * heightCm * Math.Max(quantity, 1) / 1_000_000m, 4);
    }

    private static decimal CalculateDiffPercent(decimal expected, decimal actual)
    {
        if (expected <= 0)
            return actual > 0 ? 100m : 0m;

        return Math.Round(Math.Abs(actual - expected) / expected * 100m, 2);
    }

    private static decimal? ParseTemperature(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace("°C", "", StringComparison.OrdinalIgnoreCase)
            .Replace("C", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(normalized, out var temp) ? temp : null;
    }

    private static DateTime? CalculateSlaDeadline(RouteMaster? route)
    {
        if (route == null)
            return null;

        var todayCutoff = DateTime.UtcNow.Date.Add(route.CutOffTime);
        return todayCutoff > DateTime.UtcNow ? todayCutoff : todayCutoff.AddDays(1);
    }

    private static string GenerateCode(string prefix)
    {
        return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..Math.Min($"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Length, 32)];
    }

    private static string MockPdfUrl(string documentType, string key)
    {
        return $"/mock-documents/{documentType}/{key}.pdf";
    }

    private static string CalculateAgingColor(Lpn lpn)
    {
        if (lpn.InboundTime == null)
            return "GRAY";

        var now = DateTime.UtcNow;
        var ageHours = (now - lpn.InboundTime.Value).TotalHours;
        var nearSla = lpn.SlaDeadline.HasValue && lpn.SlaDeadline.Value <= now.AddHours(6);

        if (ageHours > 48 || nearSla)
            return "RED";

        if (ageHours > 24)
            return "YELLOW";

        return "GREEN";
    }

    private static LpnResponse ToResponse(Lpn lpn, string? trackingCode)
    {
        return new LpnResponse
        {
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            OrderId = lpn.OrderId,
            TrackingCode = trackingCode,
            ItemName = lpn.Order.ItemName,
            StorageLocation = lpn.StorageLocation,
            Quantity = lpn.Quantity,
            ExpectedWeightKg = lpn.Order.ExpectedWeightKg,
            ActualWeightKg = lpn.ActualWeightKg,
            ExpectedCbm = lpn.Order.ExpectedCbm,
            ActualCbm = lpn.ActualCbm,
            MaxDiffPercent = Math.Max(
                CalculateDiffPercent(lpn.Order.ExpectedWeightKg, lpn.ActualWeightKg),
                CalculateDiffPercent(lpn.Order.ExpectedCbm, lpn.ActualCbm)),
            State = lpn.State,
            DiscrepancyReason = lpn.DiscrepancyReason,
            GrnPdfUrl = lpn.State == LpnState.RECEIVING || lpn.State == LpnState.IN_STOCK
                ? MockPdfUrl("grn", trackingCode ?? lpn.LpnCode)
                : null,
            DiscrepancyPdfUrl = lpn.State == LpnState.DISCREPANCY_HOLD
                ? MockPdfUrl("discrepancy", trackingCode ?? lpn.LpnCode)
                : null,
            ReturnPdfUrl = lpn.State == LpnState.RETURN_PENDING
                ? MockPdfUrl("return", lpn.LpnCode)
                : null,
            InboundTime = lpn.InboundTime,
            SlaDeadline = lpn.SlaDeadline,
            AgingColor = CalculateAgingColor(lpn)
        };
    }

    private static PenaltyBillResponse ToPenaltyResponse(PenaltyBill bill)
    {
        return new PenaltyBillResponse
        {
            PenaltyBillId = bill.PenaltyBillId,
            BillCode = bill.BillCode,
            LpnId = bill.LpnId,
            OrderId = bill.OrderId,
            HandlingFee = bill.HandlingFee,
            StorageFee = bill.StorageFee,
            TotalAmount = bill.TotalAmount,
            Reason = bill.Reason,
            IsPaid = bill.IsPaid,
            CreatedAt = bill.CreatedAt,
            PaidAt = bill.PaidAt
        };
    }
}
