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

        var orders = await _db.TransportOrders
            .Where(o => o.CreatedAt >= start && o.CreatedAt < endExclusive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var quotations = await _db.Quotations
            .Include(q => q.Order)
                .ThenInclude(o => o!.Customer)
            .Where(q => (q.CreatedAt >= start && q.CreatedAt < endExclusive)
                        || (q.SentAt >= start && q.SentAt < endExclusive)
                        || (q.AcceptedAt >= start && q.AcceptedAt < endExclusive))
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

        var unreadMessages = userId.HasValue
            ? await _db.ChatMessages.CountAsync(
                m => m.ReceiverId == userId.Value
                     && !m.IsRead
                     && m.CreatedAt >= start
                     && m.CreatedAt < endExclusive,
                cancellationToken)
            : 0;

        var sentQuotes = quotations
            .Where(q => q.SentAt >= start && q.SentAt < endExclusive)
            .ToList();
        var acceptedQuotes = quotations
            .Where(q => q.AcceptedAt >= start && q.AcceptedAt < endExclusive)
            .ToList();

        var funnelCounts = new[]
        {
            orders.Count,
            orders.Count(o => o.Status == "APPROVED"),
            sentQuotes.Count,
            acceptedQuotes.Count,
            contracts.Count(c => c.SentAt >= start && c.SentAt < endExclusive),
            contracts.Count(c => c.UploadedSignedAt >= start && c.UploadedSignedAt < endExclusive),
            contracts.Count(c => c.VerifiedAt >= start && c.VerifiedAt < endExclusive)
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

        var priorityContracts = await _db.CustomerContracts
            .Include(c => c.Order)
                .ThenInclude(o => o!.Customer)
            .Include(c => c.Customer)
            .Where(c => c.UploadedSignedAt.HasValue
                        && !c.VerifiedAt.HasValue
                        && (c.Status == "PENDING_SALES_VERIFICATION" || c.Status == "REQUEST_RESUBMIT"))
            .OrderBy(c => c.UploadedSignedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var priorityWorkItems = priorityContracts
            .Select(c =>
            {
                var waitingHours = Math.Max(0m, (decimal)(now - c.UploadedSignedAt!.Value).TotalHours);
                return new SalesPriorityWorkItem
                {
                    Type = "PENDING_SALES_VERIFICATION",
                    ReferenceId = c.ContractId,
                    OrderId = c.OrderId,
                    TrackingCode = c.Order?.TrackingCode,
                    CustomerName = c.Customer?.CompanyName ?? c.Order?.Customer?.CompanyName,
                    WaitingHours = Math.Round(waitingHours, 2),
                    IsOverdue = waitingHours >= 24m
                };
            })
            .ToList();

        return ApiResponse<SalesOverviewResponse>.SuccessResponse(new SalesOverviewResponse
        {
            FromDate = start,
            ToDate = endExclusive.AddTicks(-1),
            Kpis = new SalesKpis
            {
                PendingReviewOrders = orders.Count(o => o.Status == "PENDING_REVIEW"),
                NeedsUpdateOrders = orders.Count(o => o.Status == "NEEDS_UPDATE"),
                DraftQuotations = quotations.Count(q => q.Status == "DRAFT"),
                SentQuotations = quotations.Count(q => q.Status == "SENT"),
                DraftContracts = contracts.Count(c => c.Status == "DRAFT"),
                PendingCustomerSignature = contracts.Count(c => c.Status is "PENDING_CUSTOMER_SIGNATURE" or "PENDING_SIGNATURE"),
                PendingSalesVerification = contracts.Count(c => c.Status == "PENDING_SALES_VERIFICATION"),
                UnreadMessages = unreadMessages
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
            PriorityWorkItems = priorityWorkItems
        }, "Sales dashboard overview retrieved successfully");
    }

    public async Task<ApiResponse<DispatcherOverviewResponse>> GetDispatcherOverviewAsync(
        DateOnly? date,
        Guid? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = DbDate(targetDate);
        var endExclusive = start.AddDays(1);
        var now = DbNow();

        var lpnQuery = _db.Lpns.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
            lpnQuery = lpnQuery.Where(l => l.WarehouseId == warehouseId.Value);
        var lpns = await lpnQuery.ToListAsync(cancellationToken);

        var warehouseTripIds = lpns.Where(l => l.TripId.HasValue).Select(l => l.TripId!.Value).Distinct().ToHashSet();
        var tripQuery = _db.MasterTrips
            .Include(t => t.Vehicle)
            .Where(t => t.PlannedStartTime >= start && t.PlannedStartTime < endExclusive)
            .AsNoTracking()
            .AsQueryable();
        if (warehouseId.HasValue)
            tripQuery = tripQuery.Where(t => warehouseTripIds.Contains(t.TripId));
        var trips = await tripQuery.ToListAsync(cancellationToken);
        var tripIds = trips.Select(t => t.TripId).ToHashSet();

        var alerts = await _db.AlertLogs
            .Include(a => a.Trip)
                .ThenInclude(t => t!.Vehicle)
            .Where(a => a.CreatedAt >= start
                        && a.CreatedAt < endExclusive
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
        var pendingDispatcherClaims = await claimQuery.CountAsync(cancellationToken);

        var completedTrips = trips.Where(IsCompletedTrip).ToList();
        var atRiskTripIds = alerts
            .Where(a => a.Status == "NEW" || a.Status == "OPEN")
            .Where(a => a.TripId.HasValue)
            .Select(a => a.TripId!.Value)
            .ToHashSet();

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

        return ApiResponse<DispatcherOverviewResponse>.SuccessResponse(new DispatcherOverviewResponse
        {
            Kpis = new DispatcherKpis
            {
                ReadyLpns = lpns.Count(l => l.State == LpnState.IN_STOCK),
                PlannedTrips = trips.Count(t => t.Status == "PLANNED"),
                PickingTrips = trips.Count(t => t.Status == "PICKING"),
                ReadyToSealTrips = trips.Count(t => t.Status == "LOADING_COMPLETED"),
                InTransitTrips = trips.Count(t => t.Status is "IN_TRANSIT" or "DISPATCHED"),
                LateOrRiskTrips = trips.Count(t => t.Status == "DELAYED"
                                                   || (!IsCompletedTrip(t) && t.PlannedEndTime < now)
                                                   || atRiskTripIds.Contains(t.TripId)),
                AvailableVehicles = await _db.Vehicles.CountAsync(v => v.Status == "ACTIVE" || v.Status == "AVAILABLE", cancellationToken),
                AvailableDrivers = await _db.Drivers.CountAsync(d => d.Status == "ACTIVE" || d.Status == "AVAILABLE", cancellationToken),
                RedeliveryLpns = lpns.Count(l => l.State == LpnState.PENDING_REDELIVERY),
                PendingDispatcherClaims = pendingDispatcherClaims
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
                    AlertType = a.AlertType,
                    TripId = a.TripId,
                    TripCode = a.TripId.HasValue ? TripCode(a.TripId.Value) : null,
                    VehiclePlate = a.Trip?.Vehicle?.TruckPlate,
                    Message = BuildAlertMessage(a),
                    CreatedAt = a.CreatedAt
                }).ToList(),
            PriorityWorkItems = trips
                .Where(t => t.Status == "LOADING_COMPLETED")
                .OrderBy(t => t.PlannedEndTime)
                .Take(10)
                .Select(t => new DashboardWorkItem
                {
                    Type = "READY_TO_SEAL",
                    ReferenceId = t.TripId,
                    Code = TripCode(t.TripId),
                    Message = "Chuyến đã bốc hàng xong, chờ kẹp chì"
                }).ToList()
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

        HashSet<Guid>? warehouseTripIds = null;
        if (warehouseId.HasValue)
        {
            warehouseTripIds = (await _db.Lpns
                .Where(l => l.WarehouseId == warehouseId.Value && l.TripId.HasValue)
                .Select(l => l.TripId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();
        }

        var tripQuery = _db.MasterTrips
            .Include(t => t.Route)
            .Where(t => t.PlannedStartTime >= start && t.PlannedStartTime < endExclusive)
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
        if (routeId.HasValue || warehouseTripIds != null)
            incidentQuery = incidentQuery.Where(i => i.TripId.HasValue && tripIds.Contains(i.TripId.Value));
        var incidents = await incidentQuery.ToListAsync(cancellationToken);

        var claimQuery = _db.Claims
            .Include(c => c.Order)
            .Include(c => c.Lpn)
            .Where(c => c.CreatedAt >= start && c.CreatedAt < endExclusive)
            .AsNoTracking()
            .AsQueryable();
        if (routeId.HasValue)
            claimQuery = claimQuery.Where(c => c.Order != null && c.Order.MasterTrip != null && c.Order.MasterTrip.RouteId == routeId.Value);
        if (warehouseId.HasValue)
            claimQuery = claimQuery.Where(c => c.Lpn != null && c.Lpn.WarehouseId == warehouseId.Value);
        var claims = await claimQuery.ToListAsync(cancellationToken);

        var vehicleStatusDistribution = await _db.Vehicles
            .AsNoTracking()
            .GroupBy(v => v.Status ?? "UNKNOWN")
            .Select(group => new StatusCountResponse { Status = group.Key, Count = group.Count() })
            .OrderBy(item => item.Status)
            .ToListAsync(cancellationToken);
        var driverStatusDistribution = await _db.Drivers
            .AsNoTracking()
            .GroupBy(d => d.Status ?? "UNKNOWN")
            .Select(group => new StatusCountResponse { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var onlineIotDevices = await _db.IotDevices
            .CountAsync(d => d.IsOnline || d.Status == "ONLINE", cancellationToken);
        var offlineIotDevices = await _db.IotDevices
            .CountAsync(d => !d.IsOnline && d.Status != "ONLINE", cancellationToken);
        var expiringDocumentCount = await _db.VehicleDocuments.CountAsync(
            d => d.ExpireDate.HasValue
                 && d.ExpireDate.Value >= today
                 && d.ExpireDate.Value <= today.AddDays(30),
            cancellationToken);
        var expiredDocumentCount = await _db.VehicleDocuments.CountAsync(
            d => d.ExpireDate.HasValue && d.ExpireDate.Value < today,
            cancellationToken);
        var priorityDocuments = await _db.VehicleDocuments
            .Where(d => d.ExpireDate.HasValue
                        && d.ExpireDate.Value >= today
                        && d.ExpireDate.Value <= today.AddDays(30))
            .OrderBy(d => d.ExpireDate)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var usersQuery = _db.Users.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
            usersQuery = usersQuery.Where(u => u.WarehouseId == warehouseId.Value);
        var activeUsers = await usersQuery.CountAsync(
            u => u.DeletedAt == null && u.Status == "ACTIVE",
            cancellationToken);
        var inactiveUsers = await usersQuery.CountAsync(
            u => u.DeletedAt != null || u.Status == "INACTIVE",
            cancellationToken);

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
                        && i.IssuedDate <= DateOnly.FromDateTime(endExclusive.AddTicks(-1)))
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
            .GroupBy(t => new { RouteId = t.RouteId!.Value, Name = t.Route == null ? null : t.Route.OriginCity + " - " + t.Route.DestCity })
            .Select(g => new RouteTemperatureCompliance
            {
                RouteId = g.Key.RouteId,
                RouteName = g.Key.Name ?? g.Key.RouteId.ToString(),
                ComplianceRate = Percentage(g.Count(t => !tempTripIds.Contains(t.TripId)), g.Count())
            }).OrderBy(x => x.RouteName).ToList();

        var completedIn = transactions.Where(t => t.TransactionType == "IN").Sum(t => t.Amount);
        var completedOut = transactions.Where(t => t.TransactionType == "OUT").Sum(t => t.Amount);
        var unpaidAmount = invoices.Sum(i => Math.Max(0m, i.GrandTotal - (i.PaidAmount ?? 0m)));

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
                ExpiringDocuments = expiringDocumentCount,
                ExpiredDocuments = expiredDocumentCount,
                OpenIncidents = incidents.Count(i => i.Status != "RESOLVED"),
                OpenClaims = claims.Count(c => c.Status is not ("RESOLVED" or "RESOLVED_PAID" or "PAID_CLOSED" or "REJECTED")),
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
            FinancialSnapshot = new FinancialSnapshotResponse
            {
                RecognizedRevenue = invoices.Sum(i => i.GrandTotal),
                NetCashFlow = completedIn - completedOut,
                ClaimPayout = transactions.Where(t => t.TransactionType == "OUT" && t.ClaimId.HasValue).Sum(t => t.Amount),
                UnpaidInvoiceAmount = unpaidAmount
            },
            PriorityWorkItems = priorityDocuments
                .Select(d => new DashboardWorkItem
                {
                    Type = "DOCUMENT_EXPIRING",
                    ReferenceId = d.VehicleId ?? d.DocId,
                    ReferenceCode = d.DocumentNumber,
                    Message = $"{d.DocumentType} hết hạn sau {d.ExpireDate!.Value.DayNumber - today.DayNumber} ngày"
                }).ToList()
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = DateOnly.FromDateTime(start);
        var endDateInclusive = DateOnly.FromDateTime(endExclusive.AddTicks(-1));

        var invoices = await _db.Invoices
            .Where(i => i.IssuedDate >= startDate && i.IssuedDate <= endDateInclusive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var transactions = await _db.PaymentTransactions
            .Include(t => t.Claim)
            .Where(t => t.CreatedAt >= start && t.CreatedAt < endExclusive)
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
            .OrderByDescending(c => c.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        var pendingVerificationTransactionsCount = await _db.PaymentTransactions
            .CountAsync(t => t.Status == "PENDING_VERIFY", cancellationToken);
        var pendingVerificationTransactions = await _db.PaymentTransactions
            .Where(t => t.Status == "PENDING_VERIFY")
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var completedTransactions = transactions.Where(t => t.Status == "COMPLETED").ToList();
        var cashIn = completedTransactions.Where(t => t.TransactionType == "IN").ToList();
        var cashOut = completedTransactions.Where(t => t.TransactionType == "OUT").ToList();
        var driverReimbursement = incidents.Sum(i => i.ReimbursedAmount ?? i.ApprovedAmount ?? i.DriverPaidAmount);
        var claimPayout = cashOut.Where(t => t.ClaimId.HasValue).Sum(t => t.Amount);
        var receivables = invoices.Sum(OutstandingAmount);

        var cashFlow = completedTransactions
            .GroupBy(t => normalizedGroupBy == "MONTH" ? t.CreatedAt.ToString("yyyy-MM") : t.CreatedAt.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g => new CashFlowPeriod
            {
                Period = g.Key,
                CashIn = g.Where(t => t.TransactionType == "IN").Sum(t => t.Amount),
                CashOut = g.Where(t => t.TransactionType == "OUT").Sum(t => t.Amount)
            }).ToList();

        var agingDefinitions = new[]
        {
            (Bucket: "NOT_DUE", Label: "Chưa đến hạn", Predicate: (Func<Invoice, bool>)(i => i.DueDate >= today)),
            (Bucket: "OVERDUE_1_30", Label: "Quá hạn 1–30 ngày", Predicate: (Func<Invoice, bool>)(i => i.DueDate < today && today.DayNumber - i.DueDate.DayNumber <= 30)),
            (Bucket: "OVERDUE_OVER_30", Label: "Quá hạn trên 30 ngày", Predicate: (Func<Invoice, bool>)(i => i.DueDate < today && today.DayNumber - i.DueDate.DayNumber > 30))
        };
        var unpaidInvoices = invoices.Where(i => OutstandingAmount(i) > 0).ToList();

        var priorityItems = pendingClaims.Select(c => new AccountantPriorityWorkItem
            {
                Type = "PENDING_ACCOUNTANT_REVIEW",
                ReferenceId = c.ClaimId,
                ReferenceCode = c.ClaimCode,
                CreatedAt = c.CreatedAt
            })
            .Concat(pendingVerificationTransactions
                .Select(t => new AccountantPriorityWorkItem
                {
                    Type = "PENDING_TRANSACTION_VERIFICATION",
                    ReferenceId = t.TransactionId,
                    ReferenceCode = t.TransactionCode,
                    Amount = t.Amount,
                    CreatedAt = t.CreatedAt
                }))
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .ToList();

        return ApiResponse<AccountantOverviewResponse>.SuccessResponse(new AccountantOverviewResponse
        {
            Kpis = new AccountantKpis
            {
                RecognizedRevenue = invoices.Sum(i => i.GrandTotal),
                CashCollected = cashIn.Sum(t => t.Amount),
                CodCollected = cashIn.Sum(t => t.Amount),
                Receivables = receivables,
                VatAmount = invoices.Sum(i => i.TaxAmount),
                ClaimPayout = claimPayout,
                DriverReimbursement = driverReimbursement,
                NetCashFlow = cashIn.Sum(t => t.Amount) - cashOut.Sum(t => t.Amount),
                PendingAccountantClaims = pendingAccountantClaimsCount,
                PendingVerificationTransactions = pendingVerificationTransactionsCount
            },
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
            CodByPaymentMethod = cashIn.GroupBy(t => t.PaymentMethod)
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
            PriorityWorkItems = priorityItems
        }, "Accountant dashboard overview retrieved successfully");
    }

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
