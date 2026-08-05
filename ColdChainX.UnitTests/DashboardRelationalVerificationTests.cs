using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;

namespace ColdChainX.UnitTests;

public sealed class DashboardRelationalVerificationTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private DashboardService Service => new(_database.Db);

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task SalesOverview_AllSupportedSections_UseExactPersistedEventsAndBoundaries()
    {
        var start = DbTime(2026, 8, 1);
        var customer = AddCustomer("SALES");
        var salesUser = AddUser("sales-user", "ACTIVE");
        var sender = AddUser("customer-user", "ACTIVE");

        var orders = new[]
        {
            AddOrder("S-1", "PENDING_REVIEW", start, customer),
            AddOrder("S-2", "NEEDS_UPDATE", start.AddHours(1), customer),
            AddOrder("S-3", "APPROVED", start.AddHours(2), customer),
            AddOrder("S-4", "APPROVED", start.AddHours(3), customer),
            AddOrder("S-5", "APPROVED", start.AddDays(1).AddTicks(-1), customer)
        };
        AddOrder("S-OUT", "PENDING_REVIEW", start.AddDays(1), customer);

        AddQuotation("DRAFT", 100m, start.AddHours(1), orders[0]);
        AddQuotation("SENT", 200m, start.AddHours(3), orders[2], sentAt: start.AddHours(6));
        AddQuotation("ACCEPTED", 300m, start.AddHours(4), orders[3],
            sentAt: start.AddHours(7), acceptedAt: start.AddHours(8));

        AddContract("SC-DRAFT", "DRAFT", start.AddHours(5), orders[0], customer);
        AddContract("SC-SIGN", "PENDING_CUSTOMER_SIGNATURE", start.AddHours(6), orders[2], customer,
            sentAt: start.AddHours(7));
        AddContract("SC-VERIFY", "PENDING_SALES_VERIFICATION", start.AddHours(7), orders[3], customer,
            uploadedAt: start.AddHours(8));
        AddContract("SC-ACTIVE", "ACTIVE", start.AddHours(8), orders[4], customer,
            uploadedAt: start.AddHours(9), verifiedAt: start.AddHours(11));

        _database.Db.ChatMessages.AddRange(
            new ChatMessage
            {
                Id = Guid.NewGuid(), Order = orders[0], OrderId = orders[0].OrderId,
                Sender = sender, SenderId = sender.UserId, Receiver = salesUser, ReceiverId = salesUser.UserId,
                MessageContent = "Unread", CreatedAt = start, IsRead = false
            },
            new ChatMessage
            {
                Id = Guid.NewGuid(), Order = orders[0], OrderId = orders[0].OrderId,
                Sender = sender, SenderId = sender.UserId, Receiver = salesUser, ReceiverId = salesUser.UserId,
                MessageContent = "Read", CreatedAt = start, IsRead = true
            },
            new ChatMessage
            {
                Id = Guid.NewGuid(), Order = orders[0], OrderId = orders[0].OrderId,
                Sender = sender, SenderId = sender.UserId, Receiver = salesUser, ReceiverId = salesUser.UserId,
                MessageContent = "Outside", CreatedAt = start.AddDays(1), IsRead = false
            });
        await _database.Db.SaveChangesAsync();

        var result = await Service.GetSalesOverviewAsync(start, start, salesUser.UserId);

        Assert.True(result.Success);
        var data = Assert.IsType<ColdChainX.Application.DTOs.Dashboards.SalesOverviewResponse>(result.Data);
        Assert.Equal(1, data.Kpis.PendingReviewOrders);
        Assert.Equal(1, data.Kpis.NeedsUpdateOrders);
        Assert.Equal(1, data.Kpis.DraftQuotations);
        Assert.Equal(1, data.Kpis.SentQuotations);
        Assert.Equal(1, data.Kpis.DraftContracts);
        Assert.Equal(1, data.Kpis.PendingCustomerSignature);
        Assert.Equal(1, data.Kpis.PendingSalesVerification);
        Assert.Equal(1, data.Kpis.UnreadMessages);

        Assert.Equal(
            new[] { "ORDER_CREATED", "ORDER_APPROVED", "QUOTATION_SENT", "QUOTATION_ACCEPTED", "CONTRACT_SENT", "SIGNED_FILE_UPLOADED", "CONTRACT_ACTIVE" },
            data.Funnel.Select(x => x.Key));
        Assert.Equal(new[] { 5, 3, 2, 1, 1, 2, 1 }, data.Funnel.Select(x => x.Count));
        var funnel = data.Funnel.ToList();
        Assert.Equal(60m, funnel[1].ConversionRate);
        Assert.Equal(66.67m, funnel[2].ConversionRate);
        Assert.Equal(50m, funnel[3].ConversionRate);

        Assert.Equal(
            new[] { ("ACCEPTED", 1), ("DRAFT", 1), ("SENT", 1) },
            data.QuotationStatusDistribution.Select(x => (x.Status, x.Count)));
        var month = Assert.Single(data.QuotationValuesByMonth);
        Assert.Equal("2026-08", month.Month);
        Assert.Equal(500m, month.SentValue);
        Assert.Equal(300m, month.AcceptedValue);
        Assert.Equal(4m, data.AverageProcessingTimes.OrderToQuotationSentHours);
        Assert.Equal(2m, data.AverageProcessingTimes.SignedUploadToVerificationHours);
        Assert.Empty(data.ReviewReasons);
        var priority = Assert.Single(data.PriorityWorkItems);
        Assert.Equal("PENDING_SALES_VERIFICATION", priority.Type);
        Assert.Equal("Customer SALES", priority.CustomerName);
        Assert.True(priority.IsOverdue);
        Assert.True(priority.WaitingHours >= 24m);
        Assert.True(data.PriorityWorkItems.Count <= 10);
    }

    [Fact]
    public async Task SalesOverview_EmptyDataset_ReturnsZeroesEmptyCollectionsAndNoDivideByZero()
    {
        var start = DbTime(2026, 8, 1);

        var result = await Service.GetSalesOverviewAsync(start, start, null);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data.Kpis.PendingReviewOrders);
        Assert.Equal(100m, result.Data.Funnel.First().ConversionRate);
        Assert.All(result.Data.Funnel.Skip(1), stage => Assert.Equal(0m, stage.ConversionRate));
        Assert.Empty(result.Data.QuotationStatusDistribution);
        Assert.Empty(result.Data.QuotationValuesByMonth);
        Assert.Empty(result.Data.ReviewReasons);
        Assert.Empty(result.Data.PriorityWorkItems);
        Assert.Null(result.Data.AverageProcessingTimes.OrderToQuotationSentHours);
    }

    [Fact]
    public async Task DispatcherOverview_WarehouseDateKpisUtilizationAndPriorities_AreExact()
    {
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var start = DbDate(targetDate);
        var warehouse = AddWarehouse("D1");
        var receiver = AddUser("dispatcher-receiver", "ACTIVE", warehouse.WarehouseId);
        var customer = AddCustomer("DISPATCH");
        var order = AddOrder("D-ORDER", "IN_STOCK", start, customer);
        var receipt = AddReceipt(order, warehouse, receiver, start);
        var (origin, destination) = AddLocations();
        var capacityVehicle = AddVehicle("51A-000.01", "ACTIVE", 100m, 10m);
        AddVehicle("51A-000.02", "AVAILABLE", 100m, 10m);
        AddDriver("Driver Active", "ACTIVE");
        AddDriver("Driver Available", "AVAILABLE");
        AddDriver("Driver Busy", "ONTRIP");

        var planned = AddTrip("PLANNED", start, origin, destination, capacityVehicle);
        var picking = AddTrip("PICKING", start.AddMinutes(1), origin, destination);
        var loading = AddTrip("LOADING_COMPLETED", start.AddMinutes(2), origin, destination);
        var transit = AddTrip("IN_TRANSIT", start.AddMinutes(3), origin, destination);
        var delayed = AddTrip("DELAYED", start.AddMinutes(4), origin, destination);
        var completed = AddTrip("COMPLETED", start.AddMinutes(5), origin, destination, completedAt: start.AddHours(2));

        AddLpn("LPN-PLAN", LpnState.IN_STOCK, planned, order, receipt, warehouse, 50m, 2m);
        AddLpn("LPN-PICK", LpnState.LOADING, picking, order, receipt, warehouse);
        AddLpn("LPN-LOAD", LpnState.LOADING_COMPLETED, loading, order, receipt, warehouse);
        AddLpn("LPN-TRANSIT", LpnState.SHIPPING, transit, order, receipt, warehouse);
        var redelivery = AddLpn("LPN-REDELIVERY", LpnState.PENDING_REDELIVERY, delayed, order, receipt, warehouse);
        AddLpn("LPN-COMPLETE", LpnState.DELIVERED, completed, order, receipt, warehouse);

        _database.Db.AlertLogs.Add(new AlertLog
        {
            AlertId = Guid.NewGuid(), Trip = transit, TripId = transit.TripId,
            AlertType = "TEMPERATURE_HIGH", Value = 9m, Status = "OPEN", CreatedAt = start.AddHours(1)
        });
        _database.Db.Claims.Add(new Claim
        {
            ClaimId = Guid.NewGuid(), ClaimCode = "CL-DISP", Lpn = redelivery, LpnId = redelivery.LpnId,
            ClaimType = "DAMAGE", Description = "Damage", Status = "PENDING_DISPATCHER_REVIEW", CreatedAt = start
        });
        await _database.Db.SaveChangesAsync();

        var result = await Service.GetDispatcherOverviewAsync(targetDate, warehouse.WarehouseId);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.Kpis.ReadyLpns);
        Assert.Equal(1, result.Data.Kpis.PlannedTrips);
        Assert.Equal(1, result.Data.Kpis.PickingTrips);
        Assert.Equal(1, result.Data.Kpis.ReadyToSealTrips);
        Assert.Equal(1, result.Data.Kpis.InTransitTrips);
        Assert.Equal(2, result.Data.Kpis.LateOrRiskTrips);
        Assert.Equal(2, result.Data.Kpis.AvailableVehicles);
        Assert.Equal(2, result.Data.Kpis.AvailableDrivers);
        Assert.Equal(1, result.Data.Kpis.RedeliveryLpns);
        Assert.Equal(1, result.Data.Kpis.PendingDispatcherClaims);
        Assert.Equal(6, result.Data.TripStatusDistribution.Sum(x => x.Count));
        var utilization = result.Data.TripUtilization.Single(x => x.TripId == planned.TripId);
        Assert.Equal(50m, utilization.WeightUtilizationPercent);
        Assert.Equal(20m, utilization.VolumeUtilizationPercent);
        Assert.All(result.Data.TripUtilization, x =>
        {
            Assert.True(x.WeightUtilizationPercent >= 0m);
            Assert.True(x.VolumeUtilizationPercent >= 0m);
        });
        Assert.Equal(1, result.Data.DeliveryPerformance.OnTimeTrips);
        Assert.Equal(0, result.Data.DeliveryPerformance.LateTrips);
        var alert = Assert.Single(result.Data.PriorityAlerts);
        Assert.Equal("TEMPERATURE_HIGH", alert.AlertType);
        Assert.DoesNotContain("Latitude", alert.GetType().GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("Longitude", alert.GetType().GetProperties().Select(x => x.Name));
        Assert.Equal("READY_TO_SEAL", Assert.Single(result.Data.PriorityWorkItems).Type);

        var unknown = await Service.GetDispatcherOverviewAsync(targetDate, Guid.NewGuid());
        Assert.Equal(0, unknown.Data!.Kpis.ReadyLpns);
        Assert.Equal(0, unknown.Data.Kpis.PlannedTrips);
        Assert.Empty(unknown.Data.TripStatusDistribution);
    }

    [Fact]
    public async Task AdminOverview_AllSupportedKpisFiltersAndDocumentBoundaries_AreExact()
    {
        var start = DbTime(2026, 8, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var warehouse = AddWarehouse("A1");
        var otherWarehouse = AddWarehouse("A2");
        var user = AddUser("admin-active", "ACTIVE", warehouse.WarehouseId);
        AddUser("admin-inactive", "INACTIVE", warehouse.WarehouseId);
        AddUser("other-active", "ACTIVE", otherWarehouse.WarehouseId);
        var customer = AddCustomer("ADMIN");
        var order = AddOrder("A-ORDER", "IN_TRANSIT", start, customer);
        var receipt = AddReceipt(order, warehouse, user, start);
        var (origin, destination) = AddLocations();
        var route = AddRoute("R-ADMIN");
        var vehicleActive = AddVehicle("51A-100.01", "ACTIVE", 100m, 10m);
        var vehicleOnTrip = AddVehicle("51A-100.02", "ONTRIP", 100m, 10m);
        var vehicleMaintenance = AddVehicle("51A-100.03", "MAINTENANCE", 100m, 10m);
        AddDriver("Admin Driver Available", "AVAILABLE");
        AddDriver("Admin Driver OnTrip", "ONTRIP");
        AddDriver("Admin Driver Relax", "RELAXING");

        var activeTrip = AddTrip("IN_TRANSIT", start.AddHours(1), origin, destination, vehicleOnTrip, route);
        var delayedTrip = AddTrip("DELAYED", start.AddHours(2), origin, destination, vehicleActive, route);
        order.MasterTrip = activeTrip;
        order.MasterTripId = activeTrip.TripId;
        var adminLpn = AddLpn("LPN-ADMIN-1", LpnState.SHIPPING, activeTrip, order, receipt, warehouse);
        AddLpn("LPN-ADMIN-2", LpnState.SHIPPING, delayedTrip, order, receipt, warehouse);

        _database.Db.AlertLogs.Add(new AlertLog
        {
            AlertId = Guid.NewGuid(), Trip = activeTrip, TripId = activeTrip.TripId,
            AlertType = "TEMP_HIGH", Status = "OPEN", CreatedAt = start.AddHours(3)
        });
        _database.Db.IotDevices.AddRange(
            new IotDevice { DeviceId = Guid.NewGuid(), DeviceCode = "IOT-ON", Vehicle = vehicleActive, VehicleId = vehicleActive.VehicleId, IsOnline = true, Status = "ONLINE" },
            new IotDevice { DeviceId = Guid.NewGuid(), DeviceCode = "IOT-OFF", Vehicle = vehicleMaintenance, VehicleId = vehicleMaintenance.VehicleId, IsOnline = false, Status = "OFFLINE" });
        _database.Db.VehicleDocuments.AddRange(
            VehicleDocument(vehicleActive, "EXPIRING", today.AddDays(30)),
            VehicleDocument(vehicleActive, "EXPIRED", today.AddDays(-1)));
        _database.Db.IncidentReports.AddRange(
            Incident(user, "OPEN", start.AddHours(4), activeTrip),
            Incident(user, "RESOLVED", start.AddHours(5), activeTrip));
        var openClaim = Claim("CL-ADMIN-OPEN", "OPEN", start.AddHours(4), order);
        openClaim.Lpn = adminLpn;
        openClaim.LpnId = adminLpn.LpnId;
        var resolvedClaim = Claim("CL-ADMIN-DONE", "RESOLVED", start.AddHours(5), order);
        resolvedClaim.Lpn = adminLpn;
        resolvedClaim.LpnId = adminLpn.LpnId;
        _database.Db.Claims.AddRange(openClaim, resolvedClaim);
        var adminInvoice = Invoice(customer, "INV-ADMIN", new DateOnly(2026, 8, 1), 1_000m, 200m);
        _database.Db.Invoices.Add(adminInvoice);
        _database.Db.InvoiceLines.Add(new InvoiceLine
        {
            LineId = Guid.NewGuid(), Invoice = adminInvoice, InvoiceId = adminInvoice.InvoiceId,
            Order = order, OrderId = order.OrderId, ChargeType = "FREIGHT", Description = "Freight",
            Quantity = 1m, UnitPrice = 1_000m, Amount = 1_000m
        });
        _database.Db.PaymentTransactions.AddRange(
            Payment("ADMIN-IN", "IN", 500m, start.AddHours(6), order: order),
            Payment("ADMIN-OUT", "OUT", 100m, start.AddHours(7), order: order));
        await _database.Db.SaveChangesAsync();

        var result = await Service.GetAdminOverviewAsync(start, start, warehouse.WarehouseId, route.RouteId);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var kpis = result.Data.Kpis;
        Assert.Equal(2, kpis.ActiveTrips);
        Assert.Equal(2, kpis.LateTrips);
        Assert.Equal(1, kpis.TripsWithTemperatureAlerts);
        Assert.Equal(3, kpis.TotalVehicles);
        Assert.Equal(1, kpis.VehiclesOnTrip);
        Assert.Equal(1, kpis.VehiclesUnderMaintenance);
        Assert.Equal(1, kpis.AvailableDrivers);
        Assert.Equal(1, kpis.DriversOnTrip);
        Assert.Equal(1, kpis.DriversRelaxing);
        Assert.Equal(1, kpis.OnlineIotDevices);
        Assert.Equal(1, kpis.OfflineIotDevices);
        Assert.Equal(1, kpis.ExpiringDocuments);
        Assert.Equal(1, kpis.ExpiredDocuments);
        Assert.Equal(1, kpis.OpenIncidents);
        Assert.Equal(1, kpis.OpenClaims);
        Assert.Equal(1, kpis.ActiveUsers);
        Assert.Equal(1, kpis.InactiveUsers);
        Assert.Equal(3, result.Data.VehicleStatusDistribution.Sum(x => x.Count));
        Assert.Equal(2, result.Data.IotStatusDistribution.Sum(x => x.Count));
        var tripPeriod = Assert.Single(result.Data.TripPerformanceByPeriod);
        Assert.Equal(0, tripPeriod.Completed);
        Assert.Equal(0, tripPeriod.Late);
        Assert.Equal(2, tripPeriod.Incident);
        Assert.Equal(50m, Assert.Single(result.Data.TemperatureComplianceByRoute).ComplianceRate);
        Assert.Equal(1_000m, result.Data.FinancialSnapshot.RecognizedRevenue);
        Assert.Equal(400m, result.Data.FinancialSnapshot.NetCashFlow);
        Assert.Equal(1_000m, result.Data.FinancialSnapshot.UnpaidInvoiceAmount);
        Assert.Equal("DOCUMENT_EXPIRING", Assert.Single(result.Data.PriorityWorkItems).Type);

        var unknownWarehouse = await Service.GetAdminOverviewAsync(start, start, Guid.NewGuid(), route.RouteId);
        Assert.Equal(0, unknownWarehouse.Data!.Kpis.ActiveTrips);
        Assert.Equal(0, unknownWarehouse.Data.Kpis.OpenClaims);
        Assert.Equal(0, unknownWarehouse.Data.Kpis.ActiveUsers);
    }

    [Fact]
    public async Task AccountantOverview_ValidLedgerOnlyNetCashFlowAgingGroupingAndPriorities_AreExact()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-40);
        var start = DbDate(startDate);
        var end = DbDate(today);
        var customer = AddCustomer("ACCOUNTANT");
        var reporter = AddUser("accountant-reporter", "ACTIVE");
        var claim = Claim("CL-ACCOUNTANT", "PENDING_ACCOUNTANT_REVIEW", end.AddHours(1));
        _database.Db.Claims.Add(claim);
        _database.Db.Invoices.AddRange(
            Invoice(customer, "INV-NOT-DUE", today.AddDays(-2), 1_000m, 100m, today.AddDays(5), 100m),
            Invoice(customer, "INV-OVERDUE-10", today.AddDays(-20), 500m, 40m, today.AddDays(-10)),
            Invoice(customer, "INV-OVERDUE-31", today.AddDays(-35), 300m, 24m, today.AddDays(-31), 100m));
        _database.Db.PaymentTransactions.AddRange(
            Payment("ACC-IN-1", "IN", 1_000m, end.AddHours(1), "COMPLETED", "PAYOS"),
            Payment("ACC-IN-2", "IN", 200m, end.AddHours(2), "COMPLETED", "CASH"),
            Payment("ACC-CLAIM-OUT", "OUT", 100m, end.AddHours(3), "COMPLETED", "BANK_TRANSFER", claim),
            Payment("ACC-REIMBURSE-OUT", "OUT", 50m, end.AddHours(4), "COMPLETED", "BANK_TRANSFER"),
            Payment("ACC-PENDING", "IN", 9_999m, end.AddHours(5), "PENDING_VERIFY", "PAYOS"),
            Payment("ACC-FAILED", "IN", 8_888m, end.AddHours(6), "FAILED", "PAYOS"),
            Payment("ACC-CANCELLED", "OUT", 7_777m, end.AddHours(7), "CANCELLED", "BANK_TRANSFER"));
        _database.Db.IncidentReports.Add(new IncidentReport
        {
            IncidentId = Guid.NewGuid(), IncidentType = "BREAKDOWN", Severity = "MEDIUM", Description = "Repair",
            DriverPaidAmount = 60m, ApprovedAmount = 50m, ReimbursedAmount = 50m,
            ExpenseStatus = "REIMBURSED", ReimbursedAt = end.AddHours(4),
            ReportedBy = reporter.UserId, ReportedByNavigation = reporter, ReportedAt = end, Status = "RESOLVED"
        });
        await _database.Db.SaveChangesAsync();

        var daily = await Service.GetAccountantOverviewAsync(start, end, "DAY");
        var monthly = await Service.GetAccountantOverviewAsync(start, end, "MONTH");
        var invalid = await Service.GetAccountantOverviewAsync(start, end, "YEAR");

        Assert.True(daily.Success);
        Assert.NotNull(daily.Data);
        var kpis = daily.Data.Kpis;
        Assert.Equal(1_800m, kpis.RecognizedRevenue);
        Assert.Equal(1_200m, kpis.CashCollected);
        Assert.Equal(1_200m, kpis.CodCollected);
        Assert.Equal(1_600m, kpis.Receivables);
        Assert.Equal(164m, kpis.VatAmount);
        Assert.Equal(100m, kpis.ClaimPayout);
        Assert.Equal(50m, kpis.DriverReimbursement);
        Assert.Equal(1_050m, kpis.NetCashFlow);
        Assert.Equal(1, kpis.PendingAccountantClaims);
        Assert.Equal(1, kpis.PendingVerificationTransactions);
        var dailyPeriod = Assert.Single(daily.Data.CashFlowSeries);
        Assert.Equal(1_200m, dailyPeriod.CashIn);
        Assert.Equal(150m, dailyPeriod.CashOut);
        Assert.Equal(3, daily.Data.InvoiceStatusDistribution.Sum(x => x.Count));
        Assert.Equal(900m, daily.Data.ReceivablesAging.Single(x => x.Bucket == "NOT_DUE").Amount);
        Assert.Equal(500m, daily.Data.ReceivablesAging.Single(x => x.Bucket == "OVERDUE_1_30").Amount);
        Assert.Equal(200m, daily.Data.ReceivablesAging.Single(x => x.Bucket == "OVERDUE_OVER_30").Amount);
        Assert.Equal(2, daily.Data.CodByPaymentMethod.Count);
        Assert.Equal(100m, Assert.Single(daily.Data.ClaimPayoutByType).Amount);
        Assert.Equal(2, daily.Data.PriorityWorkItems.Count);
        Assert.True(monthly.Success);
        Assert.NotEmpty(monthly.Data!.CashFlowSeries);
        Assert.False(invalid.Success);
        Assert.Contains("DAY or MONTH", invalid.Message);
    }

    [Fact]
    public async Task EveryDashboard_NoMatchingPeriod_ReturnsZeroNumericValuesAndEmptyCollections()
    {
        var emptyStart = DbTime(2035, 1, 1);
        var sales = await Service.GetSalesOverviewAsync(emptyStart, emptyStart, null);
        var dispatcher = await Service.GetDispatcherOverviewAsync(new DateOnly(2035, 1, 1), Guid.NewGuid());
        var admin = await Service.GetAdminOverviewAsync(emptyStart, emptyStart, Guid.NewGuid(), Guid.NewGuid());
        var accountant = await Service.GetAccountantOverviewAsync(emptyStart, emptyStart, "DAY");

        Assert.True(sales.Success);
        Assert.Empty(sales.Data!.QuotationStatusDistribution);
        Assert.True(dispatcher.Success);
        Assert.Empty(dispatcher.Data!.TripStatusDistribution);
        Assert.True(admin.Success);
        Assert.Empty(admin.Data!.TripPerformanceByPeriod);
        Assert.True(accountant.Success);
        Assert.Empty(accountant.Data!.CashFlowSeries);
        Assert.Equal(0m, accountant.Data.Kpis.NetCashFlow);
    }

    [Fact]
    public async Task DateRangeDashboards_InvertedRange_ReturnBusinessValidationFailure()
    {
        var earlier = DbTime(2026, 8, 1);
        var later = earlier.AddDays(2);

        var sales = await Service.GetSalesOverviewAsync(later, earlier, null);
        var admin = await Service.GetAdminOverviewAsync(later, earlier, null, null);
        var accountant = await Service.GetAccountantOverviewAsync(later, earlier, "DAY");

        Assert.False(sales.Success);
        Assert.Equal(400, sales.StatusCode);
        Assert.False(admin.Success);
        Assert.Equal(400, admin.StatusCode);
        Assert.False(accountant.Success);
        Assert.Equal(400, accountant.StatusCode);
    }

    private Customer AddCustomer(string suffix)
    {
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(), CompanyName = "Customer " + suffix,
            TaxCode = "TAX-" + suffix + "-" + Guid.NewGuid().ToString("N")[..6], Status = "ACTIVE"
        };
        _database.Db.Customers.Add(customer);
        return customer;
    }

    private User AddUser(string username, string status, Guid? warehouseId = null)
    {
        var user = new User
        {
            UserId = Guid.NewGuid(), Username = username + "-" + Guid.NewGuid().ToString("N")[..6],
            FullName = username, Status = status, WarehouseId = warehouseId
        };
        _database.Db.Users.Add(user);
        return user;
    }

    private TransportOrder AddOrder(string suffix, string status, DateTime createdAt, Customer customer)
    {
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(), Customer = customer, CustomerId = customer.CustomerId,
            TrackingCode = "TRACK-" + suffix, ItemName = "Cold cargo", Category = "PHARMA",
            Quantity = 1, PackingType = "PALLET", TempCondition = "2-8C", Status = status, CreatedAt = createdAt
        };
        _database.Db.TransportOrders.Add(order);
        return order;
    }

    private void AddQuotation(string status, decimal amount, DateTime createdAt, TransportOrder order, DateTime? sentAt = null, DateTime? acceptedAt = null)
        => _database.Db.Quotations.Add(new Quotation
        {
            QuoteId = Guid.NewGuid(), Order = order, OrderId = order.OrderId, BaseFreight = amount,
            VatAmount = 0m, FinalAmount = amount, PricingSource = "AUTO", Status = status,
            CreatedAt = createdAt, SentAt = sentAt, AcceptedAt = acceptedAt
        });

    private void AddContract(string number, string status, DateTime createdAt, TransportOrder order, Customer customer,
        DateTime? sentAt = null, DateTime? uploadedAt = null, DateTime? verifiedAt = null)
        => _database.Db.CustomerContracts.Add(new CustomerContract
        {
            ContractId = Guid.NewGuid(), ContractNumber = number, Status = status, FileUrl = string.Empty,
            ExpiredDate = DateOnly.FromDateTime(createdAt.AddYears(1)), CreatedAt = createdAt,
            Order = order, OrderId = order.OrderId, Customer = customer, CustomerId = customer.CustomerId,
            SentAt = sentAt, UploadedSignedAt = uploadedAt, VerifiedAt = verifiedAt
        });

    private Warehouse AddWarehouse(string code)
    {
        var warehouse = new Warehouse
        {
            WarehouseId = Guid.NewGuid(), WarehouseName = "Warehouse " + code,
            WarehouseCode = code + Guid.NewGuid().ToString("N")[..4], WarehouseType = "COLD",
            MaxPallets = 100, Status = "ACTIVE"
        };
        _database.Db.Warehouses.Add(warehouse);
        return warehouse;
    }

    private WarehouseReceipt AddReceipt(TransportOrder order, Warehouse warehouse, User receiver, DateTime createdAt)
    {
        var receipt = new WarehouseReceipt
        {
            ReceiptId = Guid.NewGuid(), ReceiptCode = "REC-" + Guid.NewGuid().ToString("N")[..8],
            Order = order, OrderId = order.OrderId, Warehouse = warehouse, WarehouseId = warehouse.WarehouseId,
            Receiver = receiver, ReceiverId = receiver.UserId, ReceiptType = "INBOUND", DelivererName = "Carrier", CreatedAt = createdAt
        };
        _database.Db.WarehouseReceipts.Add(receipt);
        return receipt;
    }

    private (Location Origin, Location Destination) AddLocations()
    {
        var origin = new Location { LocationId = Guid.NewGuid(), Address = "Origin", Latitude = 10m, Longitude = 106m, Status = "ACTIVE" };
        var destination = new Location { LocationId = Guid.NewGuid(), Address = "Destination", Latitude = 11m, Longitude = 107m, Status = "ACTIVE" };
        _database.Db.Locations.AddRange(origin, destination);
        return (origin, destination);
    }

    private RouteMaster AddRoute(string code)
    {
        var route = new RouteMaster
        {
            RouteId = Guid.NewGuid(), RouteCode = code, OriginCity = "HCM", DestCity = "HN", TransitTime = "24h", Status = "ACTIVE"
        };
        _database.Db.RouteMasters.Add(route);
        return route;
    }

    private Vehicle AddVehicle(string plate, string status, decimal maxWeight, decimal maxCbm)
    {
        var vehicle = new Vehicle
        {
            VehicleId = Guid.NewGuid(), TruckPlate = plate, VehicleType = "REEFER", MaxWeight = maxWeight,
            MaxCbm = maxCbm, MinTemp = -20m, MaxTemp = 20m, Status = status
        };
        _database.Db.Vehicles.Add(vehicle);
        return vehicle;
    }

    private void AddDriver(string name, string status)
        => _database.Db.Drivers.Add(new Driver
        {
            DriverId = Guid.NewGuid(), FullName = name, IdentityNumber = Guid.NewGuid().ToString("N")[..12],
            PhoneNumber = "090" + Random.Shared.Next(1000000, 9999999), DateOfBirth = new DateOnly(1990, 1, 1),
            JoinDate = new DateOnly(2020, 1, 1), Status = status
        });

    private MasterTrip AddTrip(string status, DateTime plannedStart, Location origin, Location destination,
        Vehicle? vehicle = null, RouteMaster? route = null, DateTime? completedAt = null)
    {
        var trip = new MasterTrip
        {
            TripId = Guid.NewGuid(), OriginLocation = origin, OriginLocationId = origin.LocationId,
            DestinationLocation = destination, DestinationLocationId = destination.LocationId,
            Vehicle = vehicle, VehicleId = vehicle?.VehicleId, Route = route, RouteId = route?.RouteId,
            PlannedStartTime = plannedStart, PlannedEndTime = plannedStart.AddDays(2), TargetTemperature = 5m,
            Status = status, CompletedAt = completedAt
        };
        _database.Db.MasterTrips.Add(trip);
        return trip;
    }

    private Lpn AddLpn(string code, LpnState state, MasterTrip trip, TransportOrder order, WarehouseReceipt receipt,
        Warehouse warehouse, decimal weight = 0m, decimal cbm = 0m)
    {
        var lpn = new Lpn
        {
            LpnId = Guid.NewGuid(), LpnCode = code, State = state, Trip = trip, TripId = trip.TripId,
            Order = order, OrderId = order.OrderId, Receipt = receipt, ReceiptId = receipt.ReceiptId,
            Warehouse = warehouse, WarehouseId = warehouse.WarehouseId, Quantity = 1,
            ActualWeightKg = weight, ActualCbm = cbm, CreatedAt = trip.PlannedStartTime
        };
        _database.Db.Lpns.Add(lpn);
        return lpn;
    }

    private static VehicleDocument VehicleDocument(Vehicle vehicle, string number, DateOnly expiry)
        => new()
        {
            DocId = Guid.NewGuid(), Vehicle = vehicle, VehicleId = vehicle.VehicleId,
            DocumentType = "REGISTRATION", DocumentNumber = number, IssueDate = expiry.AddYears(-1),
            ExpireDate = expiry, ImageUrl = string.Empty, Status = "ACTIVE"
        };

    private static IncidentReport Incident(User reporter, string status, DateTime reportedAt, MasterTrip? trip = null)
        => new()
        {
            IncidentId = Guid.NewGuid(), IncidentType = "BREAKDOWN", Severity = "MEDIUM", Description = status,
            ExpenseStatus = "NOT_REQUIRED", ReportedBy = reporter.UserId, ReportedByNavigation = reporter,
            ReportedAt = reportedAt, Status = status, Trip = trip, TripId = trip?.TripId
        };

    private static Claim Claim(string code, string status, DateTime createdAt, TransportOrder? order = null)
        => new()
        {
            ClaimId = Guid.NewGuid(), ClaimCode = code, ClaimType = "DAMAGE", Description = code,
            Status = status, CreatedAt = createdAt, Order = order, OrderId = order?.OrderId
        };

    private static Invoice Invoice(Customer customer, string code, DateOnly issuedDate, decimal grandTotal,
        decimal tax, DateOnly? dueDate = null, decimal paid = 0m)
        => new()
        {
            InvoiceId = Guid.NewGuid(), InvoiceCode = code, Customer = customer, CustomerId = customer.CustomerId,
            SubTotal = grandTotal - tax, TaxAmount = tax, GrandTotal = grandTotal, PaidAmount = paid,
            IssuedDate = issuedDate, DueDate = dueDate ?? issuedDate.AddDays(30), Status = paid >= grandTotal ? "PAID" : "UNPAID"
        };

    private static PaymentTransaction Payment(string code, string type, decimal amount, DateTime createdAt,
        string status = "COMPLETED", string method = "BANK_TRANSFER", Claim? claim = null, TransportOrder? order = null)
        => new()
        {
            TransactionId = Guid.NewGuid(), TransactionCode = code, TransactionType = type,
            Amount = amount, CreatedAt = createdAt, CompletedAt = status == "COMPLETED" ? createdAt : null,
            Status = status, PaymentMethod = method, Claim = claim, ClaimId = claim?.ClaimId,
            Order = order, OrderId = order?.OrderId
        };

    private static DateTime DbTime(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

    private static DateTime DbDate(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
}
