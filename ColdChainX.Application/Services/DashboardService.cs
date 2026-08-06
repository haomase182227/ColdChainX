using ColdChainX.Application.DTOs.Dashboards;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Services;

public class DashboardService : IDashboardService
{
    private static readonly string[] ActiveTripStatuses =
        { "PLANNED", "PICKING", "LOADING_COMPLETED", "SEALED", "DISPATCHED", "IN_TRANSIT", "DELAYED" };

    private static readonly string[] DispatcherActiveTripStatuses =
        { "PICKING", "LOADING_COMPLETED", "SEALED", "DISPATCHED", "IN_TRANSIT", "DELAYED" };

    private static readonly string[] BusyTripStatuses =
        { "PLANNED", "PICKING", "LOADING", "LOADED", "LOADING_COMPLETED", "SEALED", "DISPATCHED", "IN_TRANSIT", "DELAYED" };

    private static readonly LpnState[] TransportReadyLpnStates =
    {
        LpnState.IN_STOCK,
        LpnState.ALLOCATED,
        LpnState.LOADING,
        LpnState.LOADING_COMPLETED,
        LpnState.RELEASED,
        LpnState.SHIPPING,
        LpnState.DELIVERED
    };

    private static readonly string[] ClosedClaimStatuses =
        { "RESOLVED", "RESOLVED_PAID", "PAID_CLOSED", "REJECTED" };

    private const int DocumentExpiryWarningDays = 15;
    private const int ClaimSlaDays = 7;
    private const int NearDepartureHours = 2;

    private readonly IApplicationDbContext _db;

    public DashboardService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<SalesOverviewResponse>> GetSalesOverviewAsync(
        DateTime? fromDate,
        DateTime? toDate,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var (start, endExclusive) = ResolveRange(fromDate, toDate);
        if (start >= endExclusive)
            return ApiResponse<SalesOverviewResponse>.Failure("fromDate must not be later than toDate.");
        var now = DbNow();

        var periodOrders = await _db.TransportOrders
            .Include(o => o.Customer)
            .Where(o => o.CreatedAt >= start && o.CreatedAt < endExclusive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var periodOrderIds = periodOrders.Select(o => o.OrderId).ToHashSet();

        var discrepancyLpns = await _db.Lpns
            .Where(l => periodOrderIds.Contains(l.OrderId)
                        && (l.State == LpnState.DISCREPANCY_HOLD
                            || (l.DiscrepancyReason != null && l.DiscrepancyReason != "")))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var discrepancyOrderIds = discrepancyLpns
            .Select(l => l.OrderId)
            .Distinct()
            .ToHashSet();
        var discrepancyAppendices = await _db.ContractAppendices
            .Where(a => discrepancyOrderIds.Contains(a.OrderId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var latestAppendixByOrder = discrepancyAppendices
            .GroupBy(a => a.OrderId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(a => a.CreatedAt).First());
        var discrepancyStatusByOrder = discrepancyOrderIds.ToDictionary(
            orderId => orderId,
            orderId => ResolveDiscrepancyDashboardStatus(
                discrepancyLpns.Where(l => l.OrderId == orderId),
                latestAppendixByOrder.GetValueOrDefault(orderId)));

        var dashboardDates = DashboardDates(start, endExclusive);
        var orderCountsByDate = periodOrders
            .Where(o => o.CreatedAt.HasValue)
            .GroupBy(o => o.CreatedAt!.Value.Date)
            .ToDictionary(group => group.Key, group => group.Count());
        var discrepancyOrdersByDate = periodOrders
            .Where(o => o.CreatedAt.HasValue && discrepancyStatusByOrder.ContainsKey(o.OrderId))
            .GroupBy(o => o.CreatedAt!.Value.Date)
            .ToDictionary(group => group.Key, group => group.ToList());

        var quotations = await _db.Quotations
            .Include(q => q.Order)
                .ThenInclude(o => o!.Customer)
            .Where(q => (q.CreatedAt >= start && q.CreatedAt < endExclusive)
                        || (q.SentAt >= start && q.SentAt < endExclusive)
                        || (q.AcceptedAt >= start && q.AcceptedAt < endExclusive))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var cohortQuotations = await _db.Quotations
            .Where(q => q.OrderId.HasValue && periodOrderIds.Contains(q.OrderId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var contracts = await _db.CustomerContracts
            .Include(c => c.Order)
                .ThenInclude(o => o!.Customer)
            .Where(c => (c.CreatedAt >= start && c.CreatedAt < endExclusive)
                        || (c.SentAt >= start && c.SentAt < endExclusive)
                        || (c.UploadedSignedAt >= start && c.UploadedSignedAt < endExclusive)
                        || (c.VerifiedAt >= start && c.VerifiedAt < endExclusive))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var cohortContracts = await _db.CustomerContracts
            .Where(c => c.OrderId.HasValue && periodOrderIds.Contains(c.OrderId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var pendingOrders = await _db.TransportOrders
            .Include(o => o.Customer)
            .Where(o => o.Status == "PENDING_REVIEW" || o.Status == "NEEDS_UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var openQuotations = await _db.Quotations
            .Include(q => q.Order)
                .ThenInclude(o => o!.Customer)
            .Where(q => q.Status == "DRAFT" || q.Status == "SENT")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var openContracts = await _db.CustomerContracts
            .Include(c => c.Order)
                .ThenInclude(o => o!.Customer)
            .Include(c => c.Customer)
            .Where(c => c.Status == "DRAFT"
                        || c.Status == "PENDING_CUSTOMER_SIGNATURE"
                        || c.Status == "PENDING_SIGNATURE"
                        || c.Status == "PENDING_SALES_VERIFICATION"
                        || c.Status == "REQUEST_RESUBMIT")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var unreadMessages = userId.HasValue
            ? await _db.ChatMessages.CountAsync(
                m => m.ReceiverId == userId.Value && !m.IsRead,
                cancellationToken)
            : 0;

        var sentQuotes = quotations
            .Where(q => q.SentAt >= start && q.SentAt < endExclusive)
            .ToList();
        var acceptedQuotes = quotations
            .Where(q => q.AcceptedAt >= start && q.AcceptedAt < endExclusive)
            .ToList();

        var verifiedOrderIds = cohortContracts
            .Where(c => c.VerifiedAt.HasValue
                        && c.VerifiedAt < endExclusive
                        && c.OrderId.HasValue)
            .Select(c => c.OrderId!.Value)
            .ToHashSet();
        var uploadedOrderIds = cohortContracts
            .Where(c => c.UploadedSignedAt.HasValue
                        && c.UploadedSignedAt < endExclusive
                        && c.OrderId.HasValue)
            .Select(c => c.OrderId!.Value)
            .Concat(verifiedOrderIds)
            .ToHashSet();
        var contractSentOrderIds = cohortContracts
            .Where(c => c.SentAt.HasValue
                        && c.SentAt < endExclusive
                        && c.OrderId.HasValue)
            .Select(c => c.OrderId!.Value)
            .Concat(uploadedOrderIds)
            .ToHashSet();
        var acceptedQuoteOrderIds = cohortQuotations
            .Where(q => q.AcceptedAt.HasValue
                        && q.AcceptedAt < endExclusive
                        && q.OrderId.HasValue)
            .Select(q => q.OrderId!.Value)
            .Concat(contractSentOrderIds)
            .ToHashSet();
        var sentQuoteOrderIds = cohortQuotations
            .Where(q => q.SentAt.HasValue
                        && q.SentAt < endExclusive
                        && q.OrderId.HasValue)
            .Select(q => q.OrderId!.Value)
            .Concat(acceptedQuoteOrderIds)
            .ToHashSet();
        var approvedOrderIds = periodOrders
            .Where(o => IsSalesOrderApprovedOrBeyond(o.Status))
            .Select(o => o.OrderId)
            .Concat(sentQuoteOrderIds)
            .ToHashSet();

        var funnelCounts = new[]
        {
            periodOrders.Count,
            approvedOrderIds.Count,
            sentQuoteOrderIds.Count,
            acceptedQuoteOrderIds.Count,
            contractSentOrderIds.Count,
            uploadedOrderIds.Count,
            verifiedOrderIds.Count
        };
        var funnelMetadata = new[]
        {
            ("ORDER_CREATED", "Đơn mới"),
            ("ORDER_APPROVED", "Đơn được duyệt"),
            ("QUOTATION_SENT", "Đã gửi báo giá"),
            ("QUOTATION_ACCEPTED", "Khách chấp nhận báo giá"),
            ("CONTRACT_SENT", "Đã gửi hợp đồng"),
            ("SIGNED_FILE_UPLOADED", "Khách đã tải bản ký"),
            ("CONTRACT_ACTIVE", "Hợp đồng có hiệu lực")
        };
        var funnel = funnelMetadata.Select((stage, index) => new SalesFunnelItem
        {
            Key = stage.Item1,
            Label = stage.Item2,
            Count = funnelCounts[index],
            ConversionRate = index == 0 ? 100m : Percentage(funnelCounts[index], funnelCounts[index - 1])
        }).ToList();

        var monthKeys = sentQuotes.Select(q => q.SentAt!.Value.ToString("yyyy-MM"))
            .Concat(acceptedQuotes.Select(q => q.AcceptedAt!.Value.ToString("yyyy-MM")))
            .Distinct()
            .OrderBy(m => m);

        var orderToQuoteHours = sentQuotes
            .Where(q => q.Order?.CreatedAt != null && q.SentAt >= q.Order.CreatedAt)
            .Select(q => (decimal)(q.SentAt!.Value - q.Order!.CreatedAt!.Value).TotalHours)
            .ToList();
        var signedToVerifiedHours = contracts
            .Where(c => c.UploadedSignedAt.HasValue
                        && c.VerifiedAt.HasValue
                        && c.VerifiedAt >= c.UploadedSignedAt)
            .Select(c => (decimal)(c.VerifiedAt!.Value - c.UploadedSignedAt!.Value).TotalHours)
            .ToList();

        var pendingReviewOrders = pendingOrders.Where(o => o.Status == "PENDING_REVIEW").ToList();
        var needsUpdateOrders = pendingOrders.Where(o => o.Status == "NEEDS_UPDATE").ToList();
        var draftQuotations = openQuotations.Where(q => q.Status == "DRAFT").ToList();
        var waitingQuotations = openQuotations.Where(q => q.Status == "SENT").ToList();
        var draftContracts = openContracts.Where(c => c.Status == "DRAFT").ToList();
        var pendingCustomerSignature = openContracts.Where(c => c.Status is "PENDING_CUSTOMER_SIGNATURE" or "PENDING_SIGNATURE").ToList();
        var pendingSalesVerification = openContracts.Where(c => c.Status == "PENDING_SALES_VERIFICATION").ToList();

        var priorityWorkItems = pendingReviewOrders
            .Select(o => BuildSalesPriorityWorkItem("PENDING_ORDER_REVIEW", o.OrderId, o.OrderId, o.TrackingCode, o.Customer?.CompanyName, o.CreatedAt, 24m, now))
            .Concat(needsUpdateOrders.Select(o => BuildSalesPriorityWorkItem("NEEDS_ORDER_UPDATE", o.OrderId, o.OrderId, o.TrackingCode, o.Customer?.CompanyName, o.CreatedAt, 24m, now)))
            .Concat(draftQuotations.Select(q => BuildSalesPriorityWorkItem("DRAFT_QUOTATION", q.QuoteId, q.OrderId, q.Order?.TrackingCode, q.Order?.Customer?.CompanyName, q.CreatedAt, 24m, now)))
            .Concat(waitingQuotations.Select(q => BuildSalesPriorityWorkItem("WAITING_QUOTATION_RESPONSE", q.QuoteId, q.OrderId, q.Order?.TrackingCode, q.Order?.Customer?.CompanyName, q.SentAt ?? q.CreatedAt, 48m, now)))
            .Concat(draftContracts.Select(c => BuildSalesPriorityWorkItem("DRAFT_CONTRACT", c.ContractId, c.OrderId, c.Order?.TrackingCode, c.Customer?.CompanyName ?? c.Order?.Customer?.CompanyName, c.CreatedAt, 24m, now)))
            .Concat(pendingCustomerSignature.Select(c => BuildSalesPriorityWorkItem("PENDING_CUSTOMER_SIGNATURE", c.ContractId, c.OrderId, c.Order?.TrackingCode, c.Customer?.CompanyName ?? c.Order?.Customer?.CompanyName, c.SentAt ?? c.CreatedAt, 48m, now)))
            .Concat(pendingSalesVerification.Select(c => BuildSalesPriorityWorkItem("PENDING_SALES_VERIFICATION", c.ContractId, c.OrderId, c.Order?.TrackingCode, c.Customer?.CompanyName ?? c.Order?.Customer?.CompanyName, c.UploadedSignedAt ?? c.CreatedAt, 24m, now)))
            .Concat(openContracts
                .Where(c => c.Status == "REQUEST_RESUBMIT")
                .Select(c => BuildSalesPriorityWorkItem("REQUEST_RESUBMIT", c.ContractId, c.OrderId, c.Order?.TrackingCode, c.Customer?.CompanyName ?? c.Order?.Customer?.CompanyName, c.VerifiedAt ?? c.UploadedSignedAt ?? c.CreatedAt, 24m, now)))
            .OrderByDescending(x => x.IsOverdue)
            .ThenByDescending(x => x.WaitingHours)
            .Take(10)
            .ToList();

        return ApiResponse<SalesOverviewResponse>.SuccessResponse(new SalesOverviewResponse
        {
            FromDate = start,
            ToDate = endExclusive.AddTicks(-1),
            Kpis = new SalesKpis
            {
                PendingReviewOrders = pendingReviewOrders.Count,
                NeedsUpdateOrders = needsUpdateOrders.Count,
                DraftQuotations = draftQuotations.Count,
                SentQuotations = waitingQuotations.Count,
                DraftContracts = draftContracts.Count,
                PendingCustomerSignature = pendingCustomerSignature.Count,
                PendingSalesVerification = pendingSalesVerification.Count,
                UnreadMessages = unreadMessages
            },
            OverdueKpis = new SalesKpis
            {
                PendingReviewOrders = pendingReviewOrders.Count(o => IsOverdue(now, o.CreatedAt, 24m)),
                NeedsUpdateOrders = needsUpdateOrders.Count(o => IsOverdue(now, o.CreatedAt, 24m)),
                DraftQuotations = draftQuotations.Count(q => IsOverdue(now, q.CreatedAt, 24m)),
                SentQuotations = waitingQuotations.Count(q => IsOverdue(now, q.SentAt ?? q.CreatedAt, 48m)),
                DraftContracts = draftContracts.Count(c => IsOverdue(now, c.CreatedAt, 24m)),
                PendingCustomerSignature = pendingCustomerSignature.Count(c => IsOverdue(now, c.SentAt ?? c.CreatedAt, 48m)),
                PendingSalesVerification = pendingSalesVerification.Count(c => IsOverdue(now, c.UploadedSignedAt ?? c.CreatedAt, 24m))
            },
            Funnel = funnel,
            QuotationStatusDistribution = quotations
                .GroupBy(q => q.Status)
                .OrderBy(g => g.Key)
                .Select(g => new StatusCountResponse { Status = g.Key, Count = g.Count() })
                .ToList(),
            QuotationValuesByMonth = monthKeys.Select(month => new QuotationValueByMonth
            {
                Month = month,
                SentValue = sentQuotes.Where(q => q.SentAt!.Value.ToString("yyyy-MM") == month).Sum(q => q.FinalAmount),
                AcceptedValue = acceptedQuotes.Where(q => q.AcceptedAt!.Value.ToString("yyyy-MM") == month).Sum(q => q.FinalAmount)
            }).ToList(),
            AverageProcessingTimes = new SalesAverageProcessingTimes
            {
                OrderToQuotationSentHours = AverageOrNull(orderToQuoteHours),
                SignedUploadToVerificationHours = AverageOrNull(signedToVerifiedHours)
            },
            // TransportOrder currently has no persisted review-reason field.
            ReviewReasons = Array.Empty<ReviewReasonCount>(),
            PriorityWorkItems = priorityWorkItems,
            WorkDistribution = new[]
            {
                new DashboardDistributionItem { Key = "PENDING_REVIEW", Label = "Chờ duyệt đơn", Count = pendingReviewOrders.Count },
                new DashboardDistributionItem { Key = "NEEDS_UPDATE", Label = "Chờ khách bổ sung", Count = needsUpdateOrders.Count },
                new DashboardDistributionItem { Key = "DRAFT_QUOTATION", Label = "Chờ gửi báo giá", Count = draftQuotations.Count },
                new DashboardDistributionItem { Key = "SENT_QUOTATION", Label = "Chờ khách phản hồi báo giá", Count = waitingQuotations.Count },
                new DashboardDistributionItem { Key = "DRAFT_CONTRACT", Label = "Chờ gửi hợp đồng", Count = draftContracts.Count },
                new DashboardDistributionItem { Key = "PENDING_CUSTOMER_SIGNATURE", Label = "Chờ khách ký hợp đồng", Count = pendingCustomerSignature.Count },
                new DashboardDistributionItem { Key = "PENDING_SALES_VERIFICATION", Label = "Chờ xác minh bản ký", Count = pendingSalesVerification.Count }
            },
            OrderVolumeSeries = dashboardDates.Select(date => new OrderVolumePeriod
            {
                Period = date.ToString("yyyy-MM-dd"),
                TotalOrders = orderCountsByDate.GetValueOrDefault(date)
            }).ToList(),
            DiscrepancySummary = new DiscrepancySummaryResponse
            {
                TotalOrders = periodOrders.Count,
                DiscrepancyOrders = discrepancyOrderIds.Count,
                DiscrepancyRate = Percentage(discrepancyOrderIds.Count, periodOrders.Count)
            },
            DiscrepancySeries = dashboardDates.Select(date =>
            {
                var orders = discrepancyOrdersByDate.GetValueOrDefault(date) ?? new List<TransportOrder>();
                return new DiscrepancyPeriod
                {
                    Period = date.ToString("yyyy-MM-dd"),
                    Pending = orders.Count(o => discrepancyStatusByOrder[o.OrderId] == "PENDING"),
                    AppendixSent = orders.Count(o => discrepancyStatusByOrder[o.OrderId] == "APPENDIX_SENT"),
                    Resolved = orders.Count(o => discrepancyStatusByOrder[o.OrderId] == "RESOLVED")
                };
            }).ToList()
        }, "Sales dashboard overview retrieved successfully");
    }

    public async Task<ApiResponse<DispatcherOverviewResponse>> GetDispatcherOverviewAsync(
        DateOnly? date,
        Guid? warehouseId,
        string? scheduleRange = "DAY",
        CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = DbDate(targetDate);
        var endExclusive = start.AddDays(1);
        var now = DbNow();
        var warehouseLocation = warehouseId?.ToString();
        var normalizedScheduleRange = (scheduleRange ?? "DAY").Trim().ToUpperInvariant();
        if (normalizedScheduleRange is not ("DAY" or "WEEK"))
            return ApiResponse<DispatcherOverviewResponse>.Failure("scheduleRange must be DAY or WEEK.");

        var scheduleStartDate = normalizedScheduleRange == "WEEK"
            ? targetDate.AddDays(-(((int)targetDate.DayOfWeek + 6) % 7))
            : targetDate;
        var scheduleStart = DbDate(scheduleStartDate);
        var scheduleEndExclusive = scheduleStart.AddDays(normalizedScheduleRange == "WEEK" ? 7 : 1);
        var warehouseNames = await _db.Warehouses
            .AsNoTracking()
            .ToDictionaryAsync(w => w.WarehouseId, w => w.WarehouseName, cancellationToken);

        var lpnQuery = _db.Lpns.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
            lpnQuery = lpnQuery.Where(l => l.WarehouseId == warehouseId.Value);
        var lpns = await lpnQuery.ToListAsync(cancellationToken);

        var warehouseTripIds = lpns.Where(l => l.TripId.HasValue).Select(l => l.TripId!.Value).Distinct().ToHashSet();
        var tripQuery = _db.MasterTrips
            .Include(t => t.Vehicle)
            .Where(t => (t.PlannedStartTime >= start && t.PlannedStartTime < endExclusive)
                        || (t.Status != null
                            && DispatcherActiveTripStatuses.Contains(t.Status)
                            && t.PlannedStartTime < endExclusive
                            && (!t.CompletedAt.HasValue || t.CompletedAt >= start)))
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            tripQuery = tripQuery.Where(t => warehouseTripIds.Contains(t.TripId));
        var trips = await tripQuery.ToListAsync(cancellationToken);
        var tripIds = trips.Select(t => t.TripId).ToHashSet();
        var activeTripByVehicle = trips
            .Where(t => t.VehicleId.HasValue && ActiveTripStatuses.Contains(t.Status ?? string.Empty))
            .GroupBy(t => t.VehicleId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.PlannedStartTime).First());

        var alerts = await _db.AlertLogs
            .Include(a => a.Trip)
                .ThenInclude(t => t!.Vehicle)
            .Where(a => ((a.CreatedAt >= start && a.CreatedAt < endExclusive)
                         || a.Status == "OPEN"
                         || a.Status == "NEW")
                        && a.TripId.HasValue
                        && tripIds.Contains(a.TripId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var claimQuery = _db.Claims
            .Include(c => c.Lpn)
            .Where(c => c.Status == "OPEN" || c.Status == "PENDING_DISPATCHER_REVIEW")
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            claimQuery = claimQuery.Where(c => c.Lpn != null && c.Lpn.WarehouseId == warehouseId.Value);
        var pendingDispatcherClaims = await claimQuery
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var openIncidents = await _db.IncidentReports
            .Include(i => i.Trip)
                .ThenInclude(t => t!.Vehicle)
            .Where(i => i.TripId.HasValue
                        && tripIds.Contains(i.TripId.Value)
                        && i.Status != "RESOLVED")
            .OrderBy(i => i.ReportedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var vehicles = await _db.Vehicles
            .Include(v => v.MasterTrips)
            .Include(v => v.MaintenanceTickets)
            .Include(v => v.IotDevices)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var availableVehicleItems = vehicles
            .Where(v => IsVehicleAvailableForDispatch(v, warehouseLocation))
            .ToList();

        var drivers = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Include(d => d.TripDrivers)
                .ThenInclude(td => td.Trip)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var availableDriverItems = drivers
            .Where(d => IsDriverAvailableForDispatch(d, warehouseLocation))
            .ToList();

        var scheduleOrders = await _db.TransportOrders
            .Include(o => o.Schedule)
                .ThenInclude(schedule => schedule!.Route)
            .Include(o => o.InboundAsns)
            .Where(o => o.ScheduleId.HasValue
                        && o.Schedule != null
                        && o.Schedule.DepartureDate >= scheduleStart
                        && o.Schedule.DepartureDate < scheduleEndExclusive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            var orderIdsAtWarehouse = lpns.Select(l => l.OrderId).ToHashSet();
            scheduleOrders = scheduleOrders
                .Where(o => orderIdsAtWarehouse.Contains(o.OrderId)
                            || o.InboundAsns.Any(a => a.WarehouseId == warehouseId.Value))
                .ToList();
        }

        var readyOrderIds = lpns
            .GroupBy(l => l.OrderId)
            .Where(group => group.All(l => TransportReadyLpnStates.Contains(l.State)))
            .Select(group => group.Key)
            .ToHashSet();
        var scheduleReadiness = scheduleOrders
            .GroupBy(o => o.ScheduleId!.Value)
            .Select(group =>
            {
                var schedule = group.First().Schedule!;
                var totalOrderIds = group.Select(o => o.OrderId).Distinct().ToList();
                var readyOrders = totalOrderIds.Count(readyOrderIds.Contains);
                return new ScheduleReadinessItem
                {
                    ScheduleId = schedule.ScheduleId,
                    ScheduleName = schedule.ScheduleName,
                    RouteId = schedule.RouteId,
                    RouteName = RouteName(schedule.Route) ?? schedule.ScheduleName,
                    DepartureAt = schedule.DepartureDate.Date.Add(schedule.DepartureTime),
                    TotalOrders = totalOrderIds.Count,
                    ReadyOrders = readyOrders,
                    NotReadyOrders = totalOrderIds.Count - readyOrders
                };
            })
            .OrderBy(item => item.DepartureAt)
            .ThenBy(item => item.RouteName)
            .ToList();

        var offlineDeviceQuery = _db.IotDevices
            .Include(d => d.Vehicle)
            .Where(d => !d.IsOnline && d.Status != "ONLINE")
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            offlineDeviceQuery = offlineDeviceQuery.Where(d => d.Vehicle != null && d.Vehicle.CurrentLocation == warehouseLocation);
        var offlineDevices = await offlineDeviceQuery
            .OrderBy(d => d.LastPingTime)
            .ToListAsync(cancellationToken);

        var completedTrips = trips.Where(IsCompletedTrip).ToList();
        var atRiskTripIds = alerts
            .Where(a => a.Status == "NEW" || a.Status == "OPEN")
            .Where(a => a.TripId.HasValue)
            .Select(a => a.TripId!.Value)
            .ToHashSet();

        var readyLpns = lpns.Where(l => l.State == LpnState.IN_STOCK && !l.TripId.HasValue).ToList();
        var redeliveryLpns = lpns.Where(l => l.State == LpnState.PENDING_REDELIVERY).ToList();

        var utilization = trips.Select(trip =>
        {
            var tripLpns = lpns.Where(l => l.TripId == trip.TripId).ToList();
            var weight = tripLpns.Sum(l => l.ActualWeightKg);
            var volume = tripLpns.Sum(l => l.ActualCbm);
            return new TripUtilizationItem
            {
                TripId = trip.TripId,
                TripCode = TripCode(trip.TripId),
                VehiclePlate = trip.Vehicle?.TruckPlate,
                WeightUtilizationPercent = trip.Vehicle?.MaxWeight > 0 ? Percentage(weight, trip.Vehicle.MaxWeight) : 0,
                VolumeUtilizationPercent = trip.Vehicle?.MaxCbm > 0 ? Percentage(volume, trip.Vehicle.MaxCbm) : 0
            };
        }).OrderByDescending(x => Math.Max(x.WeightUtilizationPercent, x.VolumeUtilizationPercent)).Take(10).ToList();

        var workItems = readyLpns.Select(l => new DashboardWorkItem
            {
                Type = "UNASSIGNED_LPN",
                ReferenceId = l.LpnId,
                ReferenceCode = l.LpnCode,
                Message = "LPN đã nhập kho và chưa được ghép chuyến",
                IsOverdue = l.SlaDeadline.HasValue && l.SlaDeadline.Value < now,
                SlaDeadline = l.SlaDeadline
            })
            .Concat(trips
                .Where(t => t.Status == "PLANNED" && t.PlannedStartTime >= now && t.PlannedStartTime <= now.AddHours(NearDepartureHours))
                .Select(t => new DashboardWorkItem
                {
                    Type = "TRIP_NEAR_DEPARTURE",
                    ReferenceId = t.TripId,
                    ReferenceCode = TripCode(t.TripId),
                    Code = TripCode(t.TripId),
                    TripId = t.TripId,
                    Message = "Chuyến sắp đến giờ xuất phát",
                    SlaDeadline = t.PlannedStartTime
                }))
            .Concat(trips
                .Where(t => t.Status == "LOADING_COMPLETED")
                .Select(t => new DashboardWorkItem
                {
                    Type = "READY_TO_SEAL",
                    ReferenceId = t.TripId,
                    ReferenceCode = TripCode(t.TripId),
                    Code = TripCode(t.TripId),
                    TripId = t.TripId,
                    Message = "Chuyến đã bốc hàng xong, chờ kẹp chì",
                    IsOverdue = t.PlannedEndTime < now,
                    SlaDeadline = t.PlannedEndTime
                }))
            .Concat(offlineDevices.Select(d => new DashboardWorkItem
            {
                Type = "IOT_OFFLINE",
                ReferenceId = d.DeviceId,
                ReferenceCode = d.DeviceCode,
                TripId = d.VehicleId.HasValue && activeTripByVehicle.TryGetValue(d.VehicleId.Value, out var trip) ? trip.TripId : null,
                Message = "Thiết bị IoT mất kết nối",
                IsOverdue = true,
                SlaDeadline = d.LastPingTime
            }))
            .Concat(trips
                .Where(t => t.Status == "DELAYED" || (!IsCompletedTrip(t) && t.PlannedEndTime < now))
                .Select(t => new DashboardWorkItem
                {
                    Type = "LATE_TRIP",
                    ReferenceId = t.TripId,
                    ReferenceCode = TripCode(t.TripId),
                    Code = TripCode(t.TripId),
                    TripId = t.TripId,
                    Message = "Chuyến đang trễ hoặc có nguy cơ trễ",
                    IsOverdue = true,
                    SlaDeadline = t.PlannedEndTime
                }))
            .Concat(openIncidents.Select(i => new DashboardWorkItem
            {
                Type = "OPEN_INCIDENT",
                ReferenceId = i.IncidentId,
                ReferenceCode = i.IncidentType,
                TripId = i.TripId,
                Message = i.Description,
                IsOverdue = IsOverdue(now, i.ReportedAt, 24m),
                SlaDeadline = i.ReportedAt?.AddHours(24)
            }))
            .Concat(pendingDispatcherClaims.Select(c => new DashboardWorkItem
            {
                Type = "PENDING_DISPATCHER_CLAIM",
                ReferenceId = c.ClaimId,
                ReferenceCode = c.ClaimCode,
                TripId = c.Lpn?.TripId,
                Message = c.Description,
                IsOverdue = IsClaimOverdue(c, DateOnly.FromDateTime(now)),
                SlaDeadline = c.CreatedAt?.AddDays(ClaimSlaDays)
            }))
            .Concat(redeliveryLpns.Select(l => new DashboardWorkItem
            {
                Type = "PENDING_REDELIVERY",
                ReferenceId = l.LpnId,
                ReferenceCode = l.LpnCode,
                TripId = l.TripId,
                Message = "Hàng no-show đã nhập lại kho và chờ tái giao",
                IsOverdue = l.SlaDeadline.HasValue && l.SlaDeadline.Value < now,
                SlaDeadline = l.SlaDeadline
            }))
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.SlaDeadline ?? DateTime.MaxValue)
            .Take(10)
            .ToList();

        return ApiResponse<DispatcherOverviewResponse>.SuccessResponse(new DispatcherOverviewResponse
        {
            Kpis = new DispatcherKpis
            {
                ReadyLpns = readyLpns.Count,
                PlannedTrips = trips.Count(t => t.Status == "PLANNED"),
                PickingTrips = trips.Count(t => t.Status == "PICKING"),
                ReadyToSealTrips = trips.Count(t => t.Status == "LOADING_COMPLETED"),
                InTransitTrips = trips.Count(t => t.Status is "IN_TRANSIT" or "DISPATCHED"),
                LateOrRiskTrips = trips.Count(t => t.Status == "DELAYED"
                                                   || (!IsCompletedTrip(t) && t.PlannedEndTime < now)
                                                   || atRiskTripIds.Contains(t.TripId)),
                AvailableVehicles = availableVehicleItems.Count,
                AvailableDrivers = availableDriverItems.Count,
                RedeliveryLpns = redeliveryLpns.Count,
                PendingDispatcherClaims = pendingDispatcherClaims.Count
            },
            TripStatusDistribution = trips.GroupBy(t => t.Status ?? "UNKNOWN")
                .OrderBy(g => g.Key)
                .Select(g => new StatusCountResponse { Status = g.Key, Count = g.Count() })
                .ToList(),
            TripUtilization = utilization,
            DeliveryPerformance = new DeliveryPerformanceResponse
            {
                OnTimeTrips = completedTrips.Count(t => t.CompletedAt <= t.PlannedEndTime),
                LateTrips = completedTrips.Count(t => t.CompletedAt > t.PlannedEndTime)
            },
            PriorityAlerts = alerts
                .Where(a => a.Status is "NEW" or "OPEN")
                .OrderByDescending(a => a.CreatedAt)
                .Take(10)
                .Select(a => new DashboardAlertItem
                {
                    AlertId = a.AlertId,
                    Severity = MapAlertSeverity(a),
                    AlertType = a.AlertType,
                    TripId = a.TripId,
                    TripCode = a.TripId.HasValue ? TripCode(a.TripId.Value) : null,
                    VehiclePlate = a.Trip?.Vehicle?.TruckPlate,
                    Message = BuildAlertMessage(a),
                    Status = a.Status,
                    CreatedAt = a.CreatedAt,
                    ActionType = "VIEW_TRIP"
                }).ToList(),
            PriorityWorkItems = workItems,
            ReadyLpnsByWarehouse = BuildWarehouseDistribution(
                readyLpns.Select(l => l.WarehouseId),
                warehouseNames),
            AvailableVehiclesByWarehouse = BuildWarehouseDistribution(
                availableVehicleItems.Select(v => ParseWarehouseLocation(v.CurrentLocation)),
                warehouseNames),
            VehicleStatusDistribution = vehicles
                .GroupBy(ResolveVehicleDashboardStatus)
                .OrderBy(group => group.Key)
                .Select(group => new StatusCountResponse { Status = group.Key, Count = group.Count() })
                .ToList(),
            AvailableDriversByWarehouse = BuildWarehouseDistribution(
                availableDriverItems.Select(d => ParseWarehouseLocation(d.CurrentLocation)),
                warehouseNames),
            DriverStatusDistribution = drivers
                .GroupBy(ResolveDriverDashboardStatus)
                .OrderBy(group => group.Key)
                .Select(group => new StatusCountResponse { Status = group.Key, Count = group.Count() })
                .ToList(),
            ScheduleReadiness = scheduleReadiness
        }, "Dispatcher dashboard overview retrieved successfully");
    }

    public async Task<ApiResponse<AdminOverviewResponse>> GetAdminOverviewAsync(
        DateTime? fromDate,
        DateTime? toDate,
        Guid? warehouseId,
        Guid? routeId,
        CancellationToken cancellationToken = default)
    {
        var (start, endExclusive) = ResolveRange(fromDate, toDate);
        if (start >= endExclusive)
            return ApiResponse<AdminOverviewResponse>.Failure("fromDate must not be later than toDate.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DbNow();
        var endDateInclusive = DateOnly.FromDateTime(endExclusive.AddTicks(-1));
        var warehouseLocation = warehouseId?.ToString();

        HashSet<Guid>? warehouseTripIds = null;
        if (warehouseId.HasValue)
        {
            warehouseTripIds = (await _db.Lpns
                .Where(l => l.WarehouseId == warehouseId.Value && l.TripId.HasValue)
                .Select(l => l.TripId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();
        }

        HashSet<Guid>? scopedTripIds = null;
        if (routeId.HasValue || warehouseTripIds != null)
        {
            var scopedTripQuery = _db.MasterTrips.AsNoTracking().AsQueryable();
            if (routeId.HasValue)
                scopedTripQuery = scopedTripQuery.Where(t => t.RouteId == routeId.Value);
            if (warehouseTripIds != null)
                scopedTripQuery = scopedTripQuery.Where(t => warehouseTripIds.Contains(t.TripId));
            scopedTripIds = (await scopedTripQuery.Select(t => t.TripId).ToListAsync(cancellationToken)).ToHashSet();
        }

        var tripQuery = _db.MasterTrips
            .Include(t => t.Route)
            .Include(t => t.Vehicle)
            .Where(t => t.PlannedStartTime < endExclusive
                        && (t.CompletedAt ?? t.PlannedEndTime) >= start)
            .AsNoTracking()
            .AsQueryable();
        if (routeId.HasValue)
            tripQuery = tripQuery.Where(t => t.RouteId == routeId.Value);
        if (warehouseTripIds != null)
            tripQuery = tripQuery.Where(t => warehouseTripIds.Contains(t.TripId));
        var trips = await tripQuery.ToListAsync(cancellationToken);
        var tripIds = trips.Select(t => t.TripId).ToHashSet();

        var alerts = await _db.AlertLogs
            .Where(a => a.CreatedAt >= start
                        && a.CreatedAt < endExclusive
                        && a.TripId.HasValue
                        && tripIds.Contains(a.TripId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var tempAlerts = alerts.Where(IsTemperatureAlert).ToList();

        var incidentQuery = _db.IncidentReports
            .Where(i => i.ReportedAt >= start && i.ReportedAt < endExclusive)
            .AsNoTracking()
            .AsQueryable();
        if (scopedTripIds != null)
            incidentQuery = incidentQuery.Where(i => i.TripId.HasValue && scopedTripIds.Contains(i.TripId.Value));
        var incidents = await incidentQuery.ToListAsync(cancellationToken);

        var openIncidentQuery = _db.IncidentReports
            .Include(i => i.Trip)
            .Where(i => i.Status == null || i.Status != "RESOLVED")
            .AsNoTracking()
            .AsQueryable();
        if (scopedTripIds != null)
            openIncidentQuery = openIncidentQuery.Where(i => i.TripId.HasValue && scopedTripIds.Contains(i.TripId.Value));
        var openIncidents = await openIncidentQuery.ToListAsync(cancellationToken);

        var openClaimQuery = _db.Claims
            .Include(c => c.Order)
            .Include(c => c.Lpn)
            .Where(c => c.Status == null || !ClosedClaimStatuses.Contains(c.Status))
            .AsNoTracking()
            .AsQueryable();
        if (routeId.HasValue)
            openClaimQuery = openClaimQuery.Where(c => c.Order != null && c.Order.MasterTrip != null && c.Order.MasterTrip.RouteId == routeId.Value);
        if (warehouseId.HasValue)
            openClaimQuery = openClaimQuery.Where(c => c.Lpn != null && c.Lpn.WarehouseId == warehouseId.Value);
        var openClaims = await openClaimQuery.ToListAsync(cancellationToken);
        var overdueClaims = openClaims.Where(c => IsClaimOverdue(c, today)).ToList();

        var vehicleQuery = _db.Vehicles
            .Include(v => v.MaintenanceTickets)
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            vehicleQuery = vehicleQuery.Where(v => v.CurrentLocation == warehouseLocation);
        var vehicles = await vehicleQuery.ToListAsync(cancellationToken);
        var vehicleStatusDistribution = vehicles
            .GroupBy(v => v.Status ?? "UNKNOWN")
            .Select(group => new StatusCountResponse { Status = group.Key, Count = group.Count() })
            .OrderBy(item => item.Status)
            .ToList();

        var driverQuery = _db.Drivers
            .Include(d => d.DriverLicenses)
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            driverQuery = driverQuery.Where(d => d.CurrentLocation == warehouseLocation);
        var drivers = await driverQuery.ToListAsync(cancellationToken);
        var driverStatusDistribution = drivers
            .GroupBy(d => d.Status ?? "UNKNOWN")
            .Select(group => new StatusCountResponse { Status = group.Key, Count = group.Count() })
            .ToList();

        var iotDeviceQuery = _db.IotDevices
            .Include(d => d.Vehicle)
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            iotDeviceQuery = iotDeviceQuery.Where(d => d.Vehicle != null && d.Vehicle.CurrentLocation == warehouseLocation);
        var iotDevices = await iotDeviceQuery.ToListAsync(cancellationToken);
        var onlineIotDevices = iotDevices.Count(d => d.IsOnline || d.Status == "ONLINE");
        var offlineIotDevices = iotDevices.Count(d => !d.IsOnline && d.Status != "ONLINE");
        var unassignedIotDevices = iotDevices.Count(d => !d.VehicleId.HasValue);

        var vehicleDocumentQuery = _db.VehicleDocuments
            .Include(d => d.Vehicle)
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            vehicleDocumentQuery = vehicleDocumentQuery.Where(d => d.Vehicle != null && d.Vehicle.CurrentLocation == warehouseLocation);
        var vehicleDocuments = await vehicleDocumentQuery.ToListAsync(cancellationToken);
        var expiringVehicleDocuments = vehicleDocuments.Count(d =>
            d.ExpireDate.HasValue
            && d.ExpireDate.Value >= today
            && d.ExpireDate.Value <= today.AddDays(DocumentExpiryWarningDays));
        var expiredVehicleDocuments = vehicleDocuments.Count(d =>
            d.ExpireDate.HasValue && d.ExpireDate.Value < today);
        var driverLicenseDocuments = drivers
            .SelectMany(d => d.DriverLicenses.Select(l => new { Driver = d, License = l }))
            .ToList();
        var expiringDriverDocuments = driverLicenseDocuments.Count(x =>
            x.License.ExpiryDate >= today
            && x.License.ExpiryDate <= today.AddDays(DocumentExpiryWarningDays));
        var expiredDriverDocuments = driverLicenseDocuments.Count(x => x.License.ExpiryDate < today);

        var usersQuery = _db.Users.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
            usersQuery = usersQuery.Where(u => u.WarehouseId == warehouseId.Value);
        var activeUsers = await usersQuery.CountAsync(
            u => u.DeletedAt == null && u.Status == "ACTIVE",
            cancellationToken);
        var inactiveUsers = await usersQuery.CountAsync(
            u => u.DeletedAt != null || u.Status == "INACTIVE",
            cancellationToken);
        var lockedUsers = await usersQuery
            .Where(u => u.Status == "LOCKED")
            .OrderByDescending(u => u.UpdatedAt ?? u.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        HashSet<Guid>? scopedOrderIds = null;
        if (routeId.HasValue || warehouseId.HasValue)
        {
            IEnumerable<Guid>? routeOrderIds = null;
            IEnumerable<Guid>? warehouseOrderIds = null;
            if (routeId.HasValue)
            {
                routeOrderIds = await _db.TransportOrders
                    .Where(o => o.MasterTrip != null && o.MasterTrip.RouteId == routeId.Value)
                    .Select(o => o.OrderId)
                    .ToListAsync(cancellationToken);
            }

            if (warehouseId.HasValue)
            {
                warehouseOrderIds = await _db.Lpns
                    .Where(l => l.WarehouseId == warehouseId.Value)
                    .Select(l => l.OrderId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            scopedOrderIds = routeOrderIds != null && warehouseOrderIds != null
                ? routeOrderIds.Intersect(warehouseOrderIds).ToHashSet()
                : (routeOrderIds ?? warehouseOrderIds ?? Array.Empty<Guid>()).ToHashSet();
        }

        var invoiceQuery = _db.Invoices
            .Where(i => i.IssuedDate >= DateOnly.FromDateTime(start)
                        && i.IssuedDate <= endDateInclusive)
            .AsNoTracking()
            .AsQueryable();
        if (scopedOrderIds != null)
            invoiceQuery = invoiceQuery.Where(i => i.InvoiceLines.Any(line => scopedOrderIds.Contains(line.OrderId)));
        var invoices = await invoiceQuery.ToListAsync(cancellationToken);

        var transactionQuery = _db.PaymentTransactions
            .Where(t => t.CreatedAt >= start && t.CreatedAt < endExclusive && t.Status == "COMPLETED")
            .AsNoTracking()
            .AsQueryable();
        if (scopedOrderIds != null)
            transactionQuery = transactionQuery.Where(t => t.OrderId.HasValue && scopedOrderIds.Contains(t.OrderId.Value));
        var transactions = await transactionQuery.ToListAsync(cancellationToken);

        var tripWarehouseLpns = await _db.Lpns
            .Include(l => l.Warehouse)
            .Where(l => l.TripId.HasValue && tripIds.Contains(l.TripId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var tripPerformance = trips
            .GroupBy(t => t.PlannedStartTime.Date)
            .OrderBy(g => g.Key)
            .Select(g => new TripPerformancePeriod
            {
                Period = g.Key.ToString("yyyy-MM-dd"),
                Completed = g.Count(IsCompletedTrip),
                Late = g.Count(t => IsCompletedTrip(t) && t.CompletedAt > t.PlannedEndTime),
                Incident = incidents.Count(i => i.TripId.HasValue && g.Any(t => t.TripId == i.TripId.Value))
            }).ToList();

        var tempTripIds = tempAlerts.Where(a => a.TripId.HasValue).Select(a => a.TripId!.Value).ToHashSet();
        var routeCompliance = trips
            .Where(t => t.RouteId.HasValue)
            .GroupBy(t => new { RouteId = t.RouteId!.Value, Name = RouteName(t.Route) })
            .Select(g => new RouteTemperatureCompliance
            {
                RouteId = g.Key.RouteId,
                RouteName = g.Key.Name ?? g.Key.RouteId.ToString(),
                ComplianceRate = Percentage(g.Count(t => !tempTripIds.Contains(t.TripId)), g.Count())
            }).OrderBy(x => x.RouteName).ToList();

        var incidentDistribution = incidents
            .GroupBy(i => i.IncidentType ?? "UNKNOWN")
            .OrderBy(g => g.Key)
            .Select(g => new IncidentDistributionItem { Type = g.Key, Count = g.Count() })
            .ToList();

        var tripsByWarehouse = tripWarehouseLpns
            .GroupBy(l => new { l.WarehouseId, Name = l.Warehouse?.WarehouseName })
            .OrderBy(g => g.Key.Name ?? "UNKNOWN")
            .Select(g => new TripsByWarehouseItem
            {
                WarehouseId = g.Key.WarehouseId,
                WarehouseName = g.Key.Name ?? "UNKNOWN",
                TripCount = g.Where(l => l.TripId.HasValue).Select(l => l.TripId!.Value).Distinct().Count(),
                OrderCount = g.Select(l => l.OrderId).Distinct().Count()
            })
            .ToList();

        var periodHours = Math.Max(1m, (decimal)(endExclusive - start).TotalHours);
        var fleetUtilization = trips
            .Where(t => t.VehicleId.HasValue)
            .GroupBy(t => new { VehicleId = t.VehicleId!.Value, Plate = t.Vehicle?.TruckPlate })
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new FleetUtilizationItem
            {
                VehicleId = g.Key.VehicleId,
                VehiclePlate = g.Key.Plate ?? g.Key.VehicleId.ToString(),
                TripCount = g.Count(),
                UtilizationRate = Percentage(g.Sum(t => TripOverlapHours(t, start, endExclusive)), periodHours)
            })
            .ToList();

        var completedIn = transactions.Where(t => t.TransactionType == "IN").Sum(t => t.Amount);
        var completedOut = transactions.Where(t => t.TransactionType == "OUT").Sum(t => t.Amount);
        var unpaidAmount = invoices.Sum(i => Math.Max(0m, i.GrandTotal - (i.PaidAmount ?? 0m)));

        var maintenanceDueVehicles = vehicles
            .Where(v => IsVehicleMaintenanceDue(v, today))
            .OrderBy(v => v.NextMaintenanceDate ?? DateOnly.MaxValue)
            .ToList();
        var priorityWorkItems = vehicleDocuments
            .Where(d => d.ExpireDate.HasValue
                        && d.ExpireDate.Value >= today
                        && d.ExpireDate.Value <= today.AddDays(DocumentExpiryWarningDays))
            .Select(d => new DashboardWorkItem
            {
                Type = "DOCUMENT_EXPIRING",
                ReferenceId = d.VehicleId ?? d.DocId,
                ReferenceCode = d.DocumentNumber,
                Message = $"{d.DocumentType} hết hạn sau {d.ExpireDate!.Value.DayNumber - today.DayNumber} ngày",
                SlaDeadline = ToDateTime(d.ExpireDate),
                IsOverdue = false
            })
            .Concat(vehicleDocuments
                .Where(d => d.ExpireDate.HasValue && d.ExpireDate.Value < today)
                .Select(d => new DashboardWorkItem
                {
                    Type = "VEHICLE_DOCUMENT_EXPIRED",
                    ReferenceId = d.VehicleId ?? d.DocId,
                    ReferenceCode = d.DocumentNumber,
                    Message = $"{d.DocumentType} đã hết hạn",
                    SlaDeadline = ToDateTime(d.ExpireDate),
                    IsOverdue = true
                }))
            .Concat(driverLicenseDocuments
                .Where(x => x.License.ExpiryDate >= today && x.License.ExpiryDate <= today.AddDays(DocumentExpiryWarningDays))
                .Select(x => new DashboardWorkItem
                {
                    Type = "DRIVER_LICENSE_EXPIRING",
                    ReferenceId = x.Driver.DriverId,
                    ReferenceCode = x.License.LicenseNumber,
                    Message = $"Bằng lái của {x.Driver.FullName} hết hạn sau {x.License.ExpiryDate.DayNumber - today.DayNumber} ngày",
                    SlaDeadline = ToDateTime(x.License.ExpiryDate),
                    IsOverdue = false
                }))
            .Concat(driverLicenseDocuments
                .Where(x => x.License.ExpiryDate < today)
                .Select(x => new DashboardWorkItem
                {
                    Type = "DRIVER_LICENSE_EXPIRED",
                    ReferenceId = x.Driver.DriverId,
                    ReferenceCode = x.License.LicenseNumber,
                    Message = $"Bằng lái của {x.Driver.FullName} đã hết hạn",
                    SlaDeadline = ToDateTime(x.License.ExpiryDate),
                    IsOverdue = true
                }))
            .Concat(maintenanceDueVehicles.Select(v => new DashboardWorkItem
            {
                Type = "VEHICLE_MAINTENANCE_DUE",
                ReferenceId = v.VehicleId,
                ReferenceCode = v.TruckPlate,
                Message = "Xe đến hạn hoặc gần đến hạn bảo dưỡng",
                SlaDeadline = ToDateTime(v.NextMaintenanceDate),
                IsOverdue = v.NextMaintenanceDate.HasValue && v.NextMaintenanceDate.Value < today
            }))
            .Concat(iotDevices.Where(d => !d.IsOnline && d.Status != "ONLINE").Select(d => new DashboardWorkItem
            {
                Type = "IOT_OFFLINE",
                ReferenceId = d.DeviceId,
                ReferenceCode = d.DeviceCode,
                Message = "Thiết bị mất kết nối",
                IsOverdue = true,
                SlaDeadline = d.LastPingTime
            }))
            .Concat(iotDevices.Where(d => !d.VehicleId.HasValue).Select(d => new DashboardWorkItem
            {
                Type = "IOT_UNASSIGNED",
                ReferenceId = d.DeviceId,
                ReferenceCode = d.DeviceCode,
                Message = "Thiết bị IoT chưa được gán xe",
                IsOverdue = true,
                SlaDeadline = d.CreatedAt
            }))
            .Concat(openIncidents.Where(i => i.Severity == "CRITICAL").Select(i => new DashboardWorkItem
            {
                Type = "CRITICAL_INCIDENT",
                ReferenceId = i.IncidentId,
                ReferenceCode = i.IncidentType,
                TripId = i.TripId,
                Message = i.Description,
                IsOverdue = true,
                SlaDeadline = i.ReportedAt
            }))
            .Concat(overdueClaims.Select(c => new DashboardWorkItem
            {
                Type = "OVERDUE_CLAIM",
                ReferenceId = c.ClaimId,
                ReferenceCode = c.ClaimCode,
                TripId = c.Lpn?.TripId,
                Message = c.Description,
                IsOverdue = true,
                SlaDeadline = c.CreatedAt?.AddDays(ClaimSlaDays)
            }))
            .Concat(lockedUsers.Select(u => new DashboardWorkItem
            {
                Type = "LOCKED_USER",
                ReferenceId = u.UserId,
                ReferenceCode = u.Username,
                Message = $"Tài khoản {u.FullName} đang bị khóa",
                IsOverdue = true,
                SlaDeadline = u.UpdatedAt ?? u.CreatedAt
            }))
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.SlaDeadline ?? DateTime.MaxValue)
            .Take(10)
            .ToList();

        return ApiResponse<AdminOverviewResponse>.SuccessResponse(new AdminOverviewResponse
        {
            Kpis = new AdminKpis
            {
                ActiveTrips = trips.Count(t => ActiveTripStatuses.Contains(t.Status ?? string.Empty)),
                LateTrips = trips.Count(t => t.Status == "DELAYED" || (!IsCompletedTrip(t) && t.PlannedEndTime < now)),
                TripsWithTemperatureAlerts = tempTripIds.Count,
                TotalVehicles = vehicleStatusDistribution.Sum(x => x.Count),
                VehiclesOnTrip = CountStatuses(vehicleStatusDistribution, "ONTRIP"),
                VehiclesUnderMaintenance = CountStatuses(vehicleStatusDistribution, "MAINTENANCE", "UNDER_MAINTENANCE"),
                AvailableDrivers = CountStatuses(driverStatusDistribution, "ACTIVE", "AVAILABLE"),
                DriversOnTrip = CountStatuses(driverStatusDistribution, "ONTRIP"),
                DriversRelaxing = CountStatuses(driverStatusDistribution, "RELAX", "RELAXING"),
                OnlineIotDevices = onlineIotDevices,
                OfflineIotDevices = offlineIotDevices,
                UnassignedIotDevices = unassignedIotDevices,
                ExpiringDocuments = expiringVehicleDocuments + expiringDriverDocuments,
                ExpiredDocuments = expiredVehicleDocuments + expiredDriverDocuments,
                ExpiringVehicleDocuments = expiringVehicleDocuments,
                ExpiredVehicleDocuments = expiredVehicleDocuments,
                ExpiringDriverDocuments = expiringDriverDocuments,
                ExpiredDriverDocuments = expiredDriverDocuments,
                OpenIncidents = openIncidents.Count,
                OpenClaims = openClaims.Count,
                OverdueClaims = overdueClaims.Count,
                ActiveUsers = activeUsers,
                InactiveUsers = inactiveUsers
            },
            VehicleStatusDistribution = vehicleStatusDistribution,
            IotStatusDistribution = new[]
            {
                new StatusCountResponse { Status = "ONLINE", Count = onlineIotDevices },
                new StatusCountResponse { Status = "OFFLINE", Count = offlineIotDevices }
            },
            TripPerformanceByPeriod = tripPerformance,
            TemperatureComplianceByRoute = routeCompliance,
            IncidentDistribution = incidentDistribution,
            TripsByWarehouse = tripsByWarehouse,
            FleetUtilization = fleetUtilization,
            FinancialSnapshot = new FinancialSnapshotResponse
            {
                RecognizedRevenue = invoices.Sum(i => i.GrandTotal),
                NetCashFlow = completedIn - completedOut,
                ClaimPayout = transactions.Where(t => t.TransactionType == "OUT" && t.ClaimId.HasValue).Sum(t => t.Amount),
                UnpaidInvoiceAmount = unpaidAmount
            },
            PriorityWorkItems = priorityWorkItems
        }, "Admin dashboard overview retrieved successfully");
    }

    public async Task<ApiResponse<AccountantOverviewResponse>> GetAccountantOverviewAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? groupBy,
        CancellationToken cancellationToken = default)
    {
        var normalizedGroupBy = string.IsNullOrWhiteSpace(groupBy) ? "DAY" : groupBy.Trim().ToUpperInvariant();
        if (normalizedGroupBy is not ("DAY" or "MONTH"))
            return ApiResponse<AccountantOverviewResponse>.Failure("groupBy must be DAY or MONTH.");

        var (start, endExclusive) = ResolveRange(fromDate, toDate);
        if (start >= endExclusive)
            return ApiResponse<AccountantOverviewResponse>.Failure("fromDate must not be later than toDate.");
        var startDate = DateOnly.FromDateTime(start);
        var endDateInclusive = DateOnly.FromDateTime(endExclusive.AddTicks(-1));
        var receivablesAsOfDate = endDateInclusive;

        var invoices = await _db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.InvoiceLines)
                .ThenInclude(line => line.Order)
                    .ThenInclude(order => order.MasterTrip)
                        .ThenInclude(trip => trip!.Route)
            .Where(i => i.IssuedDate >= startDate && i.IssuedDate <= endDateInclusive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var agingInvoices = await _db.Invoices
            .Where(i => i.IssuedDate <= receivablesAsOfDate
                        && (i.Status == null || i.Status != "CANCELLED")
                        && i.GrandTotal > (i.PaidAmount ?? 0m))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var transactions = await _db.PaymentTransactions
            .Include(t => t.Claim)
            .Where(t => (t.CompletedAt ?? t.CreatedAt) >= start
                        && (t.CompletedAt ?? t.CreatedAt) < endExclusive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var codOrderIds = (await _db.LpnDeliveryConfirmations
                .Where(c => c.CodAmount > 0)
                .Select(c => c.OrderId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Concat(await _db.DeliveryEpods
                .Where(e => e.OrderId.HasValue
                            && ((e.CodAmount ?? 0m) > 0m || (e.CodAmountPaid ?? 0m) > 0m))
                .Select(e => e.OrderId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var codInvoiceIds = (await _db.InvoiceLines
            .Where(line => codOrderIds.Contains(line.OrderId))
            .Select(line => line.InvoiceId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var persistedPaymentOrderIds = (await _db.PaymentTransactions
            .Where(t => t.OrderId.HasValue)
            .Select(t => t.OrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var paidEpodFallbacks = await _db.DeliveryEpods
            .Where(e => (e.PaymentStatus == "PAID" || e.CodAmountPaid > 0)
                        && e.OrderId.HasValue
                        && !persistedPaymentOrderIds.Contains(e.OrderId.Value)
                        && (e.PaymentConfirmedAt ?? e.CheckinTime) >= start
                        && (e.PaymentConfirmedAt ?? e.CheckinTime) < endExclusive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var incidents = await _db.IncidentReports
            .Where(i => i.ReimbursedAt >= start && i.ReimbursedAt < endExclusive && i.ExpenseStatus == "REIMBURSED")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var pendingClaimsQuery = _db.Claims
            .Where(c => c.Status == "PENDING_ACCOUNTANT_REVIEW" || c.Status == "PENDING_PAYOUT")
            .AsNoTracking()
            .AsQueryable();
        var pendingAccountantClaimsCount = await pendingClaimsQuery.CountAsync(cancellationToken);
        var pendingClaims = await pendingClaimsQuery
            .OrderBy(c => c.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        var pendingVerificationTransactionsCount = await _db.PaymentTransactions
            .CountAsync(t => t.Status == "PENDING_VERIFY", cancellationToken);
        var pendingVerificationTransactions = await _db.PaymentTransactions
            .Where(t => t.Status == "PENDING_VERIFY")
            .OrderBy(t => t.CreatedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var pendingCodConfirmations = await _db.LpnDeliveryConfirmations
            .Include(c => c.Lpn)
            .Where(c => c.CodAmount > 0 && !c.IsCodVerified)
            .OrderBy(c => c.ConfirmedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var pendingCodEpods = await _db.DeliveryEpods
            .Where(e => e.OrderId.HasValue
                        && ((e.CodAmount ?? 0m) > 0m || (e.CodAmountPaid ?? 0m) > 0m)
                        && !e.PaymentConfirmedAt.HasValue)
            .OrderBy(e => e.CreatedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var approvedDriverExpenses = await _db.IncidentReports
            .Where(i => i.ExpenseStatus == "APPROVED")
            .OrderBy(i => i.ExpenseApprovedAt ?? i.ReportedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var completedTransactions = transactions.Where(t => t.Status == "COMPLETED").ToList();
        var cashIn = completedTransactions.Where(t => t.TransactionType == "IN").ToList();
        var cashOut = completedTransactions.Where(t => t.TransactionType == "OUT").ToList();
        var codCashIn = cashIn
            .Where(t => (t.OrderId.HasValue && codOrderIds.Contains(t.OrderId.Value))
                        || (t.InvoiceId.HasValue && codInvoiceIds.Contains(t.InvoiceId.Value))
                        || IsCodTransaction(t))
            .ToList();
        var epodFallbackCashIn = paidEpodFallbacks
            .Sum(e => e.CodAmountPaid ?? e.CodAmount ?? 0m);
        var codPaymentRows = codCashIn
            .Select(t => new
            {
                PaymentMethod = t.PaymentMethod,
                Amount = t.Amount
            })
            .Concat(paidEpodFallbacks.Select(e => new
            {
                PaymentMethod = e.PaymentMethod ?? "PAYOS_QR",
                Amount = e.CodAmountPaid ?? e.CodAmount ?? 0m
            }))
            .ToList();
        var driverReimbursement = incidents.Sum(i => i.ReimbursedAmount ?? i.ApprovedAmount ?? i.DriverPaidAmount);
        var claimPayout = cashOut.Where(t => t.ClaimId.HasValue).Sum(t => t.Amount);
        var receivables = agingInvoices.Sum(OutstandingAmount);

        var cashFlowMovements = completedTransactions
            .Select(t => new
            {
                OccurredAt = TransactionOccurredAt(t),
                CashIn = t.TransactionType == "IN" ? t.Amount : 0m,
                CashOut = t.TransactionType == "OUT" ? t.Amount : 0m
            })
            .Concat(paidEpodFallbacks.Select(e => new
            {
                OccurredAt = e.PaymentConfirmedAt ?? e.CheckinTime,
                CashIn = e.CodAmountPaid ?? e.CodAmount ?? 0m,
                CashOut = 0m
            }))
            .ToList();
        var cashFlow = cashFlowMovements
            .GroupBy(t => normalizedGroupBy == "MONTH" ? t.OccurredAt.ToString("yyyy-MM") : t.OccurredAt.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g => new CashFlowPeriod
            {
                Period = g.Key,
                CashIn = g.Sum(t => t.CashIn),
                CashOut = g.Sum(t => t.CashOut)
            }).ToList();

        var agingDefinitions = new[]
        {
            (Bucket: "NOT_DUE", Label: "Chưa đến hạn", Predicate: (Func<Invoice, bool>)(i => i.DueDate >= receivablesAsOfDate)),
            (Bucket: "OVERDUE_1_30", Label: "Quá hạn 1–30 ngày", Predicate: (Func<Invoice, bool>)(i => i.DueDate < receivablesAsOfDate && receivablesAsOfDate.DayNumber - i.DueDate.DayNumber <= 30)),
            (Bucket: "OVERDUE_OVER_30", Label: "Quá hạn trên 30 ngày", Predicate: (Func<Invoice, bool>)(i => i.DueDate < receivablesAsOfDate && receivablesAsOfDate.DayNumber - i.DueDate.DayNumber > 30))
        };
        var unpaidInvoices = agingInvoices.Where(i => OutstandingAmount(i) > 0).ToList();
        var overdueInvoices = unpaidInvoices.Where(i => i.DueDate < receivablesAsOfDate).ToList();
        var notDueInvoices = unpaidInvoices.Where(i => i.DueDate >= receivablesAsOfDate).ToList();

        var topCustomersByRevenue = invoices
            .GroupBy(i => new { i.CustomerId, Name = i.Customer.CompanyName })
            .OrderByDescending(g => g.Sum(i => i.GrandTotal))
            .Take(5)
            .Select(g => new TopCustomerRevenueItem
            {
                CustomerId = g.Key.CustomerId,
                CustomerName = g.Key.Name,
                Amount = g.Sum(i => i.GrandTotal)
            })
            .ToList();

        var topRoutesByRevenue = invoices
            .SelectMany(i => i.InvoiceLines
                .Where(line => line.Order?.MasterTrip?.RouteId != null)
                .Select(line => new
                {
                    RouteId = line.Order!.MasterTrip!.RouteId!.Value,
                    Route = line.Order.MasterTrip.Route,
                    Amount = line.Amount
                }))
            .GroupBy(x => new { x.RouteId, Name = RouteName(x.Route) })
            .OrderByDescending(g => g.Sum(x => x.Amount))
            .Take(5)
            .Select(g => new TopRouteRevenueItem
            {
                RouteId = g.Key.RouteId,
                RouteName = g.Key.Name ?? g.Key.RouteId.ToString(),
                Amount = g.Sum(x => x.Amount)
            })
            .ToList();

        var priorityItems = pendingVerificationTransactions
            .Select(t => new AccountantPriorityWorkItem
            {
                Type = "PENDING_TRANSACTION_VERIFICATION",
                ReferenceId = t.TransactionId,
                ReferenceCode = t.TransactionCode,
                Amount = t.Amount,
                CreatedAt = t.CreatedAt,
                IsOverdue = IsOverdue(DbNow(), t.CreatedAt, 24m)
            })
            .Concat(notDueInvoices
                .OrderBy(i => i.DueDate)
                .Take(10)
                .Select(i => new AccountantPriorityWorkItem
                {
                    Type = "UNPAID_INVOICE",
                    ReferenceId = i.InvoiceId,
                    ReferenceCode = i.InvoiceCode,
                    Amount = OutstandingAmount(i),
                    CreatedAt = i.CreatedAt,
                    DueDate = i.DueDate,
                    IsOverdue = false
                }))
            .Concat(overdueInvoices
                .OrderBy(i => i.DueDate)
                .Take(10)
                .Select(i => new AccountantPriorityWorkItem
                {
                    Type = "OVERDUE_INVOICE",
                    ReferenceId = i.InvoiceId,
                    ReferenceCode = i.InvoiceCode,
                    Amount = OutstandingAmount(i),
                    CreatedAt = i.CreatedAt,
                    DueDate = i.DueDate,
                    IsOverdue = true
                }))
            .Concat(pendingCodConfirmations.Select(c => new AccountantPriorityWorkItem
            {
                Type = "COD_PENDING_HANDOVER",
                ReferenceId = c.ConfirmationId,
                ReferenceCode = c.Lpn.LpnCode,
                Amount = c.CodAmount,
                CreatedAt = c.ConfirmedAt,
                DueDate = DateOnly.FromDateTime(c.ConfirmedAt.AddDays(1)),
                IsOverdue = c.ConfirmedAt.AddDays(1) < DbNow()
            }))
            .Concat(pendingCodEpods.Select(e => new AccountantPriorityWorkItem
            {
                Type = "COD_PENDING_HANDOVER",
                ReferenceId = e.EpodId,
                ReferenceCode = e.OrderId?.ToString() ?? e.EpodId.ToString(),
                Amount = e.CodAmountPaid ?? e.CodAmount,
                CreatedAt = e.CreatedAt,
                DueDate = e.CreatedAt.HasValue ? DateOnly.FromDateTime(e.CreatedAt.Value.AddDays(1)) : null,
                IsOverdue = e.CreatedAt.HasValue && e.CreatedAt.Value.AddDays(1) < DbNow()
            }))
            .Concat(pendingClaims.Select(c => new AccountantPriorityWorkItem
            {
                Type = c.Status == "PENDING_PAYOUT" ? "CLAIM_PAYOUT_NEAR_SLA" : "PENDING_ACCOUNTANT_REVIEW",
                ReferenceId = c.ClaimId,
                ReferenceCode = c.ClaimCode,
                CreatedAt = c.CreatedAt,
                DueDate = c.CreatedAt.HasValue ? DateOnly.FromDateTime(c.CreatedAt.Value.AddDays(ClaimSlaDays)) : null,
                IsOverdue = IsClaimOverdue(c, receivablesAsOfDate)
            }))
            .Concat(approvedDriverExpenses.Select(i => new AccountantPriorityWorkItem
            {
                Type = "APPROVED_DRIVER_EXPENSE",
                ReferenceId = i.IncidentId,
                ReferenceCode = i.IncidentType,
                Amount = i.ApprovedAmount ?? i.DriverPaidAmount,
                CreatedAt = i.ExpenseApprovedAt ?? i.ReportedAt,
                IsOverdue = IsOverdue(DbNow(), i.ExpenseApprovedAt ?? i.ReportedAt, 24m)
            }))
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.DueDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.CreatedAt ?? DateTime.MaxValue)
            .Take(10)
            .ToList();

        return ApiResponse<AccountantOverviewResponse>.SuccessResponse(new AccountantOverviewResponse
        {
            Kpis = new AccountantKpis
            {
                RecognizedRevenue = invoices.Sum(i => i.GrandTotal),
                CashCollected = cashIn.Sum(t => t.Amount) + epodFallbackCashIn,
                CodCollected = codPaymentRows.Sum(t => t.Amount),
                Receivables = receivables,
                VatAmount = invoices.Sum(i => i.TaxAmount),
                ClaimPayout = claimPayout,
                DriverReimbursement = driverReimbursement,
                NetCashFlow = cashIn.Sum(t => t.Amount) + epodFallbackCashIn - cashOut.Sum(t => t.Amount),
                PendingAccountantClaims = pendingAccountantClaimsCount,
                PendingVerificationTransactions = pendingVerificationTransactionsCount
            },
            ReceivablesAsOfDate = receivablesAsOfDate,
            CashFlowSeries = cashFlow,
            InvoiceStatusDistribution = invoices
                .GroupBy(i => i.Status ?? "UNKNOWN")
                .OrderBy(g => g.Key)
                .Select(g => new InvoiceStatusDistributionItem
                {
                    Status = g.Key,
                    Count = g.Count(),
                    Amount = g.Sum(i => i.GrandTotal)
                }).ToList(),
            ReceivablesAging = agingDefinitions.Select(definition =>
            {
                var matches = unpaidInvoices.Where(definition.Predicate).ToList();
                return new ReceivablesAgingItem
                {
                    Bucket = definition.Bucket,
                    Label = definition.Label,
                    InvoiceCount = matches.Count,
                    Amount = matches.Sum(OutstandingAmount)
                };
            }).ToList(),
            CodByPaymentMethod = codPaymentRows.GroupBy(t => t.PaymentMethod)
                .OrderBy(g => g.Key)
                .Select(g => new PaymentMethodSummary
                {
                    PaymentMethod = g.Key,
                    Count = g.Count(),
                    Amount = g.Sum(t => t.Amount)
                }).ToList(),
            ClaimPayoutByType = cashOut.Where(t => t.ClaimId.HasValue)
                .GroupBy(t => t.Claim?.ClaimType ?? "UNKNOWN")
                .OrderBy(g => g.Key)
                .Select(g => new ClaimPayoutTypeSummary
                {
                    ClaimType = g.Key,
                    Count = g.Count(),
                    Amount = g.Sum(t => t.Amount)
                }).ToList(),
            TopCustomersByRevenue = topCustomersByRevenue,
            TopRoutesByRevenue = topRoutesByRevenue,
            PriorityWorkItems = priorityItems
        }, "Accountant dashboard overview retrieved successfully");
    }

    private static SalesPriorityWorkItem BuildSalesPriorityWorkItem(
        string type,
        Guid referenceId,
        Guid? orderId,
        string? trackingCode,
        string? customerName,
        DateTime? waitingSince,
        decimal overdueAfterHours,
        DateTime now)
    {
        var waitingHours = WaitingHours(now, waitingSince);
        return new SalesPriorityWorkItem
        {
            Type = type,
            ReferenceId = referenceId,
            OrderId = orderId,
            TrackingCode = trackingCode,
            CustomerName = customerName,
            WaitingHours = waitingHours,
            IsOverdue = waitingHours >= overdueAfterHours
        };
    }

    private static bool IsSalesOrderApprovedOrBeyond(string? status)
        => status is "APPROVED"
            or "QUOTATION_SENT"
            or "QUOTATION_ACCEPTED"
            or "CONTRACT_PENDING"
            or "CONTRACT_SENT"
            or "SIGNED_FILE_UPLOADED"
            or "CONTRACT_ACTIVE";

    private static decimal WaitingHours(DateTime now, DateTime? waitingSince)
        => waitingSince.HasValue
            ? Math.Round(Math.Max(0m, (decimal)(now - waitingSince.Value).TotalHours), 2)
            : 0m;

    private static bool IsOverdue(DateTime now, DateTime? waitingSince, decimal overdueAfterHours)
        => waitingSince.HasValue && WaitingHours(now, waitingSince) >= overdueAfterHours;

    private static IReadOnlyCollection<DateTime> DashboardDates(DateTime start, DateTime endExclusive)
    {
        var firstDate = start.Date;
        var lastDate = endExclusive.AddTicks(-1).Date;
        var days = Math.Max(0, (lastDate - firstDate).Days);
        return Enumerable.Range(0, days + 1)
            .Select(offset => firstDate.AddDays(offset))
            .ToList();
    }

    private static string ResolveDiscrepancyDashboardStatus(
        IEnumerable<Lpn> lpns,
        ContractAppendix? latestAppendix)
    {
        if (string.Equals(latestAppendix?.Status, "EXECUTED", StringComparison.OrdinalIgnoreCase))
            return "RESOLVED";

        var hasPendingLpn = lpns.Any(l => l.State == LpnState.DISCREPANCY_HOLD);
        if (!hasPendingLpn)
            return "RESOLVED";

        return latestAppendix?.Status is "SENT" or "ACCEPTED" or "REJECTED"
            ? "APPENDIX_SENT"
            : "PENDING";
    }

    private static Guid? ParseWarehouseLocation(string? location)
        => Guid.TryParse(location, out var warehouseId) ? warehouseId : null;

    private static IReadOnlyCollection<WarehouseResourceCount> BuildWarehouseDistribution(
        IEnumerable<Guid?> warehouseIds,
        IReadOnlyDictionary<Guid, string> warehouseNames)
    {
        return warehouseIds
            .Select(id => id.HasValue && warehouseNames.ContainsKey(id.Value) ? id : null)
            .GroupBy(id => id)
            .Select(group => new WarehouseResourceCount
            {
                WarehouseId = group.Key,
                WarehouseName = group.Key.HasValue
                    ? warehouseNames[group.Key.Value]
                    : "Chưa xác định kho",
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.WarehouseName)
            .ToList();
    }

    private static string ResolveVehicleDashboardStatus(Vehicle vehicle)
    {
        if (vehicle.MasterTrips.Any(IsBusyTrip)
            || vehicle.Status is "ONTRIP" or "ON_TRIP")
            return "ON_TRIP";

        if (vehicle.Status == "SUSPENDED_DOCS")
            return "DOCUMENT_ISSUE";

        if (IsVehicleUnderMaintenance(vehicle))
            return "MAINTENANCE";

        if (vehicle.Status == "ACTIVE" && vehicle.IotDevices.Count == 0)
            return "IOT_MISSING";

        if (IsVehicleAvailableForDispatch(vehicle, null))
            return "AVAILABLE";

        if (vehicle.Status == "PLANNING")
            return "PLANNING";

        return "INACTIVE";
    }

    private static string ResolveDriverDashboardStatus(Driver driver)
    {
        if (driver.TripDrivers.Any(td => td.Trip != null && IsBusyTrip(td.Trip))
            || driver.Status is "ONTRIP" or "ON_TRIP" or "PLANNING")
            return "ON_TRIP";

        if (driver.Status == "SUSPENDED_DOCS" || !HasValidDriverLicense(driver))
            return "DOCUMENT_ISSUE";

        if (driver.Status is "RELAX" or "RELAXING")
            return "RESTING";

        if (IsDriverAvailableForDispatch(driver, null))
            return "AVAILABLE";

        return "INACTIVE";
    }

    private static bool IsVehicleAvailableForDispatch(
        Vehicle vehicle,
        string? warehouseLocation)
    {
        if (vehicle.Status != "ACTIVE")
            return false;

        if (!string.IsNullOrWhiteSpace(warehouseLocation)
            && !string.Equals(vehicle.CurrentLocation, warehouseLocation, StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsVehicleUnderMaintenance(vehicle))
            return false;

        if (vehicle.IotDevices.Count == 0)
            return false;

        return !vehicle.MasterTrips.Any(IsBusyTrip);
    }

    private static bool IsDriverAvailableForDispatch(
        Driver driver,
        string? warehouseLocation)
    {
        if (driver.Status is not ("ACTIVE" or "AVAILABLE"))
            return false;

        if (!string.IsNullOrWhiteSpace(warehouseLocation)
            && !string.Equals(driver.CurrentLocation, warehouseLocation, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!HasValidDriverLicense(driver))
            return false;

        return !driver.TripDrivers.Any(td => td.Trip != null && IsBusyTrip(td.Trip));
    }

    private static bool IsBusyTrip(MasterTrip trip)
        => trip.Status != null
           && BusyTripStatuses.Contains(trip.Status);

    private static bool HasValidDriverLicense(Driver driver)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return driver.DriverLicenses.Any(l =>
            l.ExpiryDate >= today
            && (string.IsNullOrWhiteSpace(l.Status) || l.Status == "ACTIVE"));
    }

    private static bool IsVehicleUnderMaintenance(Vehicle vehicle)
        => vehicle.Status is "MAINTENANCE" or "UNDER_MAINTENANCE"
           || vehicle.MaintenanceTickets.Any(t =>
               !t.CompletionDate.HasValue
               && t.Status is null or "OPEN" or "PENDING" or "IN_PROGRESS");

    private static bool IsClaimOverdue(Claim claim, DateOnly asOfDate)
        => claim.CreatedAt.HasValue
           && DateOnly.FromDateTime(claim.CreatedAt.Value.AddDays(ClaimSlaDays)) < asOfDate;

    private static string MapAlertSeverity(AlertLog alert)
    {
        if (alert.AlertType.Contains("TEMP", StringComparison.OrdinalIgnoreCase)
            || alert.AlertType.Contains("DOOR", StringComparison.OrdinalIgnoreCase)
            || alert.AlertType.Contains("SOS", StringComparison.OrdinalIgnoreCase))
            return "CRITICAL";

        return alert.Status == "OPEN" ? "WARNING" : "INFO";
    }

    private static string? RouteName(RouteMaster? route)
        => route == null ? null : $"{route.OriginCity} - {route.DestCity}";

    private static decimal TripOverlapHours(MasterTrip trip, DateTime start, DateTime endExclusive)
    {
        var tripStart = trip.StartedAt ?? trip.PlannedStartTime;
        var tripEnd = trip.CompletedAt ?? trip.PlannedEndTime;
        var overlapStart = tripStart > start ? tripStart : start;
        var overlapEnd = tripEnd < endExclusive ? tripEnd : endExclusive;
        return overlapEnd <= overlapStart ? 0m : (decimal)(overlapEnd - overlapStart).TotalHours;
    }

    private static DateTime? ToDateTime(DateOnly? value)
        => value?.ToDateTime(TimeOnly.MinValue);

    private static DateTime ToDateTime(DateOnly value)
        => value.ToDateTime(TimeOnly.MinValue);

    private static bool IsVehicleMaintenanceDue(Vehicle vehicle, DateOnly today)
    {
        var dateDue = vehicle.NextMaintenanceDate.HasValue
                      && vehicle.NextMaintenanceDate.Value <= today.AddDays(vehicle.WarningDaysBeforeDue);
        var odometerDue = vehicle.NextMaintenanceOdometer > 0
                          && vehicle.CurrentOdometer >= vehicle.NextMaintenanceOdometer - vehicle.WarningKmBeforeDue;
        return dateDue || odometerDue;
    }

    private static bool IsCodTransaction(PaymentTransaction transaction)
        => ContainsCod(transaction.TransactionCode)
           || ContainsCod(transaction.ReferenceCode)
           || ContainsCod(transaction.Note);

    private static DateTime TransactionOccurredAt(PaymentTransaction transaction)
        => transaction.CompletedAt ?? transaction.CreatedAt;

    private static bool ContainsCod(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains("COD", StringComparison.OrdinalIgnoreCase);

    private static (DateTime Start, DateTime EndExclusive) ResolveRange(DateTime? fromDate, DateTime? toDate)
    {
        var now = DbNow();
        var start = AsUnspecified(fromDate ?? now.AddDays(-30));
        var suppliedEnd = AsUnspecified(toDate ?? now);
        var endExclusive = toDate.HasValue && suppliedEnd.TimeOfDay == TimeSpan.Zero
            ? suppliedEnd.Date.AddDays(1)
            : suppliedEnd.AddTicks(1);
        return (start, endExclusive);
    }

    private static DateTime AsUnspecified(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static DateTime DbNow()
        => AsUnspecified(DateTime.UtcNow);

    private static DateTime DbDate(DateOnly value)
        => DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

    private static decimal Percentage(decimal value, decimal total)
        => total <= 0 ? 0m : Math.Round(value / total * 100m, 2);

    private static decimal? AverageOrNull(IReadOnlyCollection<decimal> values)
        => values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private static int CountStatuses(IEnumerable<StatusCountResponse> distribution, params string[] statuses)
        => distribution.Where(item => statuses.Contains(item.Status)).Sum(item => item.Count);

    private static bool IsCompletedTrip(MasterTrip trip)
        => trip.CompletedAt.HasValue || trip.Status is "COMPLETED" or "RECONCILED";

    private static bool IsTemperatureAlert(AlertLog alert)
        => alert.AlertType.Contains("TEMP", StringComparison.OrdinalIgnoreCase)
           || alert.AlertType.Contains("TEMPERATURE", StringComparison.OrdinalIgnoreCase);

    private static string TripCode(Guid tripId)
        => $"TRIP-{tripId.ToString("N")[..8].ToUpperInvariant()}";

    private static string BuildAlertMessage(AlertLog alert)
        => alert.Value.HasValue
            ? $"{alert.AlertType}: {alert.Value.Value:0.##}"
            : alert.AlertType;

    private static decimal OutstandingAmount(Invoice invoice)
        => Math.Max(0m, invoice.GrandTotal - (invoice.PaidAmount ?? 0m));
}
