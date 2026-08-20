using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdChainX.API.Extensions
{
    public static class UnhappyScenarioSeeder
    {
        // Scenario Deterministic Namespace IDs
        private static readonly Guid OriginWhId = Guid.Parse("d0000000-0000-0000-0000-000000000001");
        private static readonly Guid RetWhThuDucId = Guid.Parse("d0000000-0000-0000-0000-000000000002");
        private static readonly Guid RetWhBinhTanId = Guid.Parse("d0000000-0000-0000-0000-000000000003");
        private static readonly Guid RetWhVsipId = Guid.Parse("d0000000-0000-0000-0000-000000000004");
        private static readonly Guid RetWhLongAnId = Guid.Parse("d0000000-0000-0000-0000-000000000005");
        private static readonly Guid RetWhCatLaiId = Guid.Parse("d0000000-0000-0000-0000-000000000006");

        private static readonly Guid OriginLocId = Guid.Parse("d0000000-0000-0000-0000-000000000010");
        private static readonly Guid Stop1PartialLocId = Guid.Parse("d0000000-0000-0000-0000-000000000011");
        private static readonly Guid Stop2PartialLocId = Guid.Parse("d0000000-0000-0000-0000-000000000012");
        private static readonly Guid StopFullLocId = Guid.Parse("d0000000-0000-0000-0000-000000000013");
        private static readonly Guid StopNoShowLocId = Guid.Parse("d0000000-0000-0000-0000-000000000014");

        // Shared Identities
        private static readonly Guid SharedAdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid SharedWarehouseWorkerUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid SharedCustomerUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid SharedCustomerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        // Driver 1 (Scenario A: Partial Delivery)
        private static readonly Guid Driver1UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid Driver1Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
        private static readonly Guid License1Id = Guid.Parse("5a000000-0000-0000-0000-000000000001");
        private static readonly Guid Vehicle1Id = Guid.Parse("77777777-7777-7777-7777-777777777777");
        private static readonly Guid IotDevice1Id = Guid.Parse("d0000000-0000-0000-0000-000000000050");

        // Driver 2 (Scenario B: Full Rejection)
        private static readonly Guid Driver2UserId = Guid.Parse("44444444-4444-4444-4444-444444444442");
        private static readonly Guid Driver2Id = Guid.Parse("55555555-5555-5555-5555-555555555552");
        private static readonly Guid License2Id = Guid.Parse("5a000000-0000-0000-0000-000000000002");
        private static readonly Guid Vehicle2Id = Guid.Parse("77777777-7777-7777-7777-777777777772");
        private static readonly Guid IotDevice2Id = Guid.Parse("d0000000-0000-0000-0000-000000000052");

        // Driver 3 (Scenario C: Customer No-Show)
        private static readonly Guid Driver3UserId = Guid.Parse("44444444-4444-4444-4444-444444444443");
        private static readonly Guid Driver3Id = Guid.Parse("55555555-5555-5555-5555-555555555553");
        private static readonly Guid License3Id = Guid.Parse("5a000000-0000-0000-0000-000000000003");
        private static readonly Guid Vehicle3Id = Guid.Parse("77777777-7777-7777-7777-777777777773");
        private static readonly Guid IotDevice3Id = Guid.Parse("d0000000-0000-0000-0000-000000000053");

        // Scenario A (Partial Delivery - 2 Stops)
        public static readonly Guid TripPartialId = Guid.Parse("d1000000-0000-0000-0000-000000000001");
        public static readonly Guid StopPartial1Id = Guid.Parse("d1000000-0000-0000-0000-000000000011");
        public static readonly Guid StopPartial2Id = Guid.Parse("d1000000-0000-0000-0000-000000000012");
        public static readonly Guid OrderPartial1Id = Guid.Parse("d1000000-0000-0000-0000-000000000021");
        public static readonly Guid OrderPartial2Id = Guid.Parse("d1000000-0000-0000-0000-000000000022");
        public static readonly Guid ReceiptPartial1Id = Guid.Parse("d1000000-0000-0000-0000-000000000061");
        public static readonly Guid ReceiptPartial2Id = Guid.Parse("d1000000-0000-0000-0000-000000000062");
        public static readonly Guid LpnPartial1Id = Guid.Parse("d1000000-0000-0000-0000-000000000031");
        public static readonly Guid LpnPartial2Id = Guid.Parse("d1000000-0000-0000-0000-000000000032");
        public static readonly Guid QuotePartial1Id = Guid.Parse("d1000000-0000-0000-0000-000000000041");
        public static readonly Guid QuotePartial2Id = Guid.Parse("d1000000-0000-0000-0000-000000000042");
        public static readonly Guid SealPartialId = Guid.Parse("d1000000-0000-0000-0000-000000000071");
        public static readonly Guid TelemetryPartialId = Guid.Parse("d1000000-0000-0000-0000-000000000081");

        // Scenario B (Full Rejection - 1 Stop)
        public static readonly Guid TripFullId = Guid.Parse("d2000000-0000-0000-0000-000000000001");
        public static readonly Guid StopFullId = Guid.Parse("d2000000-0000-0000-0000-000000000011");
        public static readonly Guid OrderFullId = Guid.Parse("d2000000-0000-0000-0000-000000000021");
        public static readonly Guid ReceiptFullId = Guid.Parse("d2000000-0000-0000-0000-000000000061");
        public static readonly Guid LpnFullId = Guid.Parse("d2000000-0000-0000-0000-000000000031");
        public static readonly Guid QuoteFullId = Guid.Parse("d2000000-0000-0000-0000-000000000041");
        public static readonly Guid SealFullId = Guid.Parse("d2000000-0000-0000-0000-000000000071");
        public static readonly Guid TelemetryFullId = Guid.Parse("d2000000-0000-0000-0000-000000000081");

        // Scenario C (No-Show - 1 Stop)
        public static readonly Guid TripNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000001");
        public static readonly Guid StopNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000011");
        public static readonly Guid OrderNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000021");
        public static readonly Guid ReceiptNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000061");
        public static readonly Guid LpnNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000031");
        public static readonly Guid QuoteNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000041");
        public static readonly Guid SealNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000071");
        public static readonly Guid TelemetryNoShowId = Guid.Parse("d3000000-0000-0000-0000-000000000081");

        public static async Task SeedUnhappyScenariosAsync(this IServiceProvider services, ILogger logger, IHostEnvironment env)
        {
            if (!env.IsDevelopment())
            {
                logger.LogWarning("[DEV SEEDER] Unhappy scenario seeding is strictly restricted to Development environment. Current: {Env}", env.EnvironmentName);
                return;
            }

            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

            logger.LogInformation("==================================================================");
            logger.LogInformation(">>> STARTING DEV UNHAPPY SCENARIO SEEDER (IDEMPOTENT RESET) <<<");
            logger.LogInformation("==================================================================");

            try
            {
                // Step 1: Clean up scenario-owned records in FK-safe reverse order
                await ResetScenarioRecordsAsync(db, logger);

                // Step 2: Ensure shared master baseline (Users, Fleet, Warehouses, Locations, IoT)
                await EnsureSharedMasterDataAsync(db, passwordHasher, logger);

                var d1 = await db.Drivers.FirstAsync(d => d.PhoneNumber == "0900000001");
                var d2 = await db.Drivers.FirstAsync(d => d.PhoneNumber == "0900000002");
                var d3 = await db.Drivers.FirstAsync(d => d.PhoneNumber == "0900000003");

                var v1 = await db.Vehicles.FirstAsync(v => v.TruckPlate == "51C-99999");
                var v2 = await db.Vehicles.FirstAsync(v => v.TruckPlate == "51C-88888");
                var v3 = await db.Vehicles.FirstAsync(v => v.TruckPlate == "51C-77777");

                var i1 = await db.IotDevices.FirstAsync(i => i.DeviceCode == "IOT-HYUNDAI-01");
                var i2 = await db.IotDevices.FirstAsync(i => i.DeviceCode == "IOT-ISUZU-02");
                var i3 = await db.IotDevices.FirstAsync(i => i.DeviceCode == "IOT-HINO-03");

                // Step 3: Seed Scenario A (Partial Delivery)
                SeedScenarioPartial(db, d1.DriverId, v1.VehicleId, i1.DeviceId, logger);

                // Step 4: Seed Scenario B (Full Rejection)
                SeedScenarioFullReject(db, d2.DriverId, v2.VehicleId, i2.DeviceId, logger);

                // Step 5: Seed Scenario C (No-Show)
                SeedScenarioNoShow(db, d3.DriverId, v3.VehicleId, i3.DeviceId, logger);

                await db.SaveChangesAsync();

                logger.LogInformation("==================================================================");
                logger.LogInformation(">>> UNHAPPY CASE DEV DATA SEEDING COMPLETE & READY FOR TEST <<<");
                logger.LogInformation("==================================================================");
                PrintScenarioSummary(logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DEV SEEDER ERROR] Failed to seed unhappy delivery scenarios.");
                throw;
            }
        }

        private static async Task ResetScenarioRecordsAsync(ApplicationDbContext db, ILogger logger)
        {
            logger.LogInformation("[DEV SEEDER] Clearing previous scenario runtime executions...");

            var scenarioTripIds = new[] { TripPartialId, TripFullId, TripNoShowId };
            var scenarioOrderIds = new[] { OrderPartial1Id, OrderPartial2Id, OrderFullId, OrderNoShowId };
            var scenarioStopIds = new[] { StopPartial1Id, StopPartial2Id, StopFullId, StopNoShowId };
            var scenarioLpnIds = new[] { LpnPartial1Id, LpnPartial2Id, LpnFullId, LpnNoShowId };

            // 1. PaymentTransactions (linked to scenario OrderIds)
            var transactions = await db.PaymentTransactions
                .Where(p => p.OrderId.HasValue && scenarioOrderIds.Contains(p.OrderId.Value))
                .ToListAsync();
            if (transactions.Any()) db.PaymentTransactions.RemoveRange(transactions);

            // 2. ClaimEvidences & Claims (linked to scenario orders/lpns)
            var claims = await db.Claims
                .Where(c => (c.OrderId.HasValue && scenarioOrderIds.Contains(c.OrderId.Value)) || (c.LpnId.HasValue && scenarioLpnIds.Contains(c.LpnId.Value)))
                .ToListAsync();
            var claimIds = claims.Select(c => c.ClaimId).ToList();
            if (claimIds.Any())
            {
                var evidences = await db.ClaimEvidences.Where(ce => ce.ClaimId.HasValue && claimIds.Contains(ce.ClaimId.Value)).ToListAsync();
                if (evidences.Any()) db.ClaimEvidences.RemoveRange(evidences);
                db.Claims.RemoveRange(claims);
            }

            // 3. InboundReturnSlips
            var returnSlips = await db.InboundReturnSlips
                .Where(r => scenarioOrderIds.Contains(r.OrderId) || scenarioLpnIds.Contains(r.LpnId))
                .ToListAsync();
            if (returnSlips.Any()) db.InboundReturnSlips.RemoveRange(returnSlips);

            // 4. DeliveryEpods & ReturnedItems
            var epods = await db.DeliveryEpods.Where(e => e.OrderId.HasValue && scenarioOrderIds.Contains(e.OrderId.Value)).ToListAsync();
            var epodIds = epods.Select(e => e.EpodId).ToList();
            if (epodIds.Any())
            {
                var retItems = await db.ReturnedItems.Where(ri => ri.EpodId.HasValue && epodIds.Contains(ri.EpodId.Value)).ToListAsync();
                if (retItems.Any()) db.ReturnedItems.RemoveRange(retItems);
                db.DeliveryEpods.RemoveRange(epods);
            }

            // 5. TripStopEvents & DetentionCharges
            var stopEvents = await db.TripStopEvents.Where(tse => scenarioStopIds.Contains(tse.StopId)).ToListAsync();
            if (stopEvents.Any()) db.TripStopEvents.RemoveRange(stopEvents);

            var detentions = await db.DetentionCharges.Where(dc => scenarioStopIds.Contains(dc.StopId)).ToListAsync();
            if (detentions.Any()) db.DetentionCharges.RemoveRange(detentions);

            // 6. AlertLogs (referencing scenario trips/lpns)
            var alertLogs = await db.AlertLogs.Where(al => (al.TripId.HasValue && scenarioTripIds.Contains(al.TripId.Value)) || (al.LpnId.HasValue && scenarioLpnIds.Contains(al.LpnId.Value))).ToListAsync();
            if (alertLogs.Any()) db.AlertLogs.RemoveRange(alertLogs);

            // 7. IncidentReports & IncidentEvidences
            var incidents = await db.IncidentReports.Where(ir => ir.TripId.HasValue && scenarioTripIds.Contains(ir.TripId.Value)).ToListAsync();
            var incidentIds = incidents.Select(i => i.IncidentId).ToList();
            if (incidentIds.Any())
            {
                var incEvidences = await db.IncidentEvidences.Where(ie => incidentIds.Contains(ie.IncidentId)).ToListAsync();
                if (incEvidences.Any()) db.IncidentEvidences.RemoveRange(incEvidences);
                db.IncidentReports.RemoveRange(incidents);
            }

            // 8. ExpenseAdvances & ExpenseReceipts
            var advances = await db.ExpenseAdvances.Where(ea => scenarioTripIds.Contains(ea.TripId)).ToListAsync();
            var advanceIds = advances.Select(a => a.AdvanceId).ToList();
            if (advanceIds.Any())
            {
                var receiptsExp = await db.ExpenseReceipts.Where(er => er.AdvanceId.HasValue && advanceIds.Contains(er.AdvanceId.Value)).ToListAsync();
                if (receiptsExp.Any()) db.ExpenseReceipts.RemoveRange(receiptsExp);
                db.ExpenseAdvances.RemoveRange(advances);
            }

            // 9. Notifications & ChatMessages
            var notifications = await db.Notifications.Where(n => n.OrderId.HasValue && scenarioOrderIds.Contains(n.OrderId.Value)).ToListAsync();
            if (notifications.Any()) db.Notifications.RemoveRange(notifications);

            var chatMessages = await db.ChatMessages.Where(cm => scenarioOrderIds.Contains(cm.OrderId)).ToListAsync();
            if (chatMessages.Any()) db.ChatMessages.RemoveRange(chatMessages);

            // 10. PenaltyBills & LpnDeliveryConfirmations
            var penalties = await db.PenaltyBills.Where(pb => (pb.LpnId.HasValue && scenarioLpnIds.Contains(pb.LpnId.Value)) || (pb.OrderId.HasValue && scenarioOrderIds.Contains(pb.OrderId.Value))).ToListAsync();
            if (penalties.Any()) db.PenaltyBills.RemoveRange(penalties);

            var lpnConfirms = await db.LpnDeliveryConfirmations.Where(lc => scenarioOrderIds.Contains(lc.OrderId) || scenarioLpnIds.Contains(lc.LpnId) || scenarioTripIds.Contains(lc.TripId)).ToListAsync();
            if (lpnConfirms.Any()) db.LpnDeliveryConfirmations.RemoveRange(lpnConfirms);

            // 11. Seals
            var seals = await db.Seals.Where(s => s.TripId.HasValue && scenarioTripIds.Contains(s.TripId.Value)).ToListAsync();
            if (seals.Any()) db.Seals.RemoveRange(seals);

            // 12. TelemetryLogs
            var telemetries = await db.TelemetryLogs.Where(t => t.TripId.HasValue && scenarioTripIds.Contains(t.TripId.Value)).ToListAsync();
            if (telemetries.Any()) db.TelemetryLogs.RemoveRange(telemetries);

            // 13. Lpns
            var lpns = await db.Lpns.Where(l => scenarioLpnIds.Contains(l.LpnId) || (l.TripId.HasValue && scenarioTripIds.Contains(l.TripId.Value))).ToListAsync();
            if (lpns.Any()) db.Lpns.RemoveRange(lpns);

            // 14. WarehouseReceipts (prior inbound receipts for scenario orders)
            var receipts = await db.WarehouseReceipts.Where(wr => scenarioOrderIds.Contains(wr.OrderId)).ToListAsync();
            if (receipts.Any()) db.WarehouseReceipts.RemoveRange(receipts);

            // 15. Quotations
            var quotes = await db.Quotations.Where(q => q.OrderId.HasValue && scenarioOrderIds.Contains(q.OrderId.Value)).ToListAsync();
            if (quotes.Any()) db.Quotations.RemoveRange(quotes);

            // 16. TransportOrders
            var orders = await db.TransportOrders.Where(o => scenarioOrderIds.Contains(o.OrderId) || (o.MasterTripId.HasValue && scenarioTripIds.Contains(o.MasterTripId.Value))).ToListAsync();
            if (orders.Any()) db.TransportOrders.RemoveRange(orders);

            // 17. TripStops
            var stops = await db.TripStops.Where(ts => scenarioStopIds.Contains(ts.StopId) || (ts.TripId.HasValue && scenarioTripIds.Contains(ts.TripId.Value))).ToListAsync();
            if (stops.Any()) db.TripStops.RemoveRange(stops);

            // 18. TripDrivers
            var tripDrivers = await db.TripDrivers.Where(td => scenarioTripIds.Contains(td.TripId)).ToListAsync();
            if (tripDrivers.Any()) db.TripDrivers.RemoveRange(tripDrivers);

            // 19. MasterTrips
            var trips = await db.MasterTrips.Where(t => scenarioTripIds.Contains(t.TripId)).ToListAsync();
            if (trips.Any()) db.MasterTrips.RemoveRange(trips);

            await db.SaveChangesAsync();
            logger.LogInformation("[DEV SEEDER] Previous scenario records cleaned up successfully.");
        }

        private static async Task EnsureSharedMasterDataAsync(ApplicationDbContext db, IPasswordHasher<User> passwordHasher, ILogger logger)
        {
            logger.LogInformation("[DEV SEEDER] Ensuring shared master data (Users, Roles, Fleet, Warehouses, Locations, IoT)...");

            // 0. Ensure Roles exist
            var roles = new[] { "Admin", "Customer", "Driver", "Dispatcher", "Sales", "WarehouseWorker", "Accountant" };
            foreach (var r in roles)
            {
                if (!await db.Roles.AnyAsync(x => x.RoleName == r))
                {
                    db.Roles.Add(new Role { RoleId = Guid.NewGuid(), RoleName = r });
                }
            }
            await db.SaveChangesAsync();

            var adminRole = await db.Roles.FirstAsync(r => r.RoleName == "Admin");
            var customerRole = await db.Roles.FirstAsync(r => r.RoleName == "Customer");
            var driverRole = await db.Roles.FirstAsync(r => r.RoleName == "Driver");
            var whRole = await db.Roles.FirstAsync(r => r.RoleName == "WarehouseWorker");

            // 1. Ensure Standard Default Users
            var usersToSeed = new[]
            {
                new { Id = SharedAdminUserId, Username = "admin01", Email = "admin01@coldchainx.com", Name = "System Admin", RoleId = adminRole.RoleId, WarehouseId = (Guid?)null },
                new { Id = SharedWarehouseWorkerUserId, Username = "warehouseworker01", Email = "warehouseworker01@coldchainx.com", Name = "Warehouse Worker", RoleId = whRole.RoleId, WarehouseId = (Guid?)OriginWhId },
                new { Id = SharedCustomerUserId, Username = "customer01", Email = "customer01@coldchainx.com", Name = "Vinamilk Customer", RoleId = customerRole.RoleId, WarehouseId = (Guid?)null },
                new { Id = Driver1UserId, Username = "driver01", Email = "driver01@coldchainx.com", Name = "Nguyen Van Tai (Driver 1)", RoleId = driverRole.RoleId, WarehouseId = (Guid?)null },
                new { Id = Driver2UserId, Username = "driver02", Email = "driver02@coldchainx.com", Name = "Tran Van Binh (Driver 2)", RoleId = driverRole.RoleId, WarehouseId = (Guid?)null },
                new { Id = Driver3UserId, Username = "driver03", Email = "driver03@coldchainx.com", Name = "Le Hoang Nam (Driver 3)", RoleId = driverRole.RoleId, WarehouseId = (Guid?)null },
            };

            foreach (var u in usersToSeed)
            {
                var existingUser = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.UserId == u.Id || (x.Email != null && x.Email.ToLower() == u.Email.ToLower()) || x.Username.ToLower() == u.Username.ToLower());
                if (existingUser == null)
                {
                    var user = new User
                    {
                        UserId = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        FullName = u.Name,
                        RoleId = u.RoleId,
                        WarehouseId = u.WarehouseId,
                        Status = "ACTIVE",
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    };
                    user.PasswordHash = passwordHasher.HashPassword(user, "Password@123");
                    db.Users.Add(user);
                    logger.LogInformation("[DEV SEEDER] Created user {Email} ({Role})", u.Email, u.Username);
                }
                else
                {
                    existingUser.Email = u.Email;
                    existingUser.Username = u.Username;
                    existingUser.FullName = u.Name;
                    existingUser.RoleId = u.RoleId;
                    existingUser.WarehouseId = u.WarehouseId;
                    existingUser.Status = "ACTIVE";
                    existingUser.DeletedAt = null;
                    existingUser.DeletedBy = null;
                    existingUser.PasswordHash = passwordHasher.HashPassword(existingUser, "Password@123");
                    logger.LogInformation("[DEV SEEDER] Updated & activated user {Email} ({Role})", u.Email, u.Username);
                }
            }
            await db.SaveChangesAsync();

            var u1 = await db.Users.FirstAsync(u => u.Email == "driver01@coldchainx.com");
            var u2 = await db.Users.FirstAsync(u => u.Email == "driver02@coldchainx.com");
            var u3 = await db.Users.FirstAsync(u => u.Email == "driver03@coldchainx.com");

            // 2. Ensure 3 Vehicles
            var vehicleConfigs = new[]
            {
                new { Id = Vehicle1Id, Plate = "51C-99999", Brand = "Hyundai", Type = "REEFER_TRUCK", Weight = 5000m, Cbm = 30m, MinT = -20m, MaxT = 8m },
                new { Id = Vehicle2Id, Plate = "51C-88888", Brand = "Isuzu", Type = "REEFER_TRUCK", Weight = 3500m, Cbm = 20m, MinT = -20m, MaxT = 8m },
                new { Id = Vehicle3Id, Plate = "51C-77777", Brand = "Hino", Type = "REEFER_TRUCK", Weight = 5000m, Cbm = 30m, MinT = -25m, MaxT = 5m },
            };

            foreach (var vc in vehicleConfigs)
            {
                var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == vc.Id || v.TruckPlate == vc.Plate);
                if (vehicle == null)
                {
                    db.Vehicles.Add(new Vehicle
                    {
                        VehicleId = vc.Id,
                        TruckPlate = vc.Plate,
                        Brand = vc.Brand,
                        VehicleType = vc.Type,
                        MaxWeight = vc.Weight,
                        MaxCbm = vc.Cbm,
                        MinTemp = vc.MinT,
                        MaxTemp = vc.MaxT,
                        CurrentOdometer = 12000,
                        NextMaintenanceOdometer = 20000,
                        Status = "ACTIVE",
                        CurrentLocation = OriginWhId.ToString(),
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    });
                }
                else
                {
                    vehicle.Status = "ACTIVE";
                    vehicle.CurrentLocation = OriginWhId.ToString();
                }
            }

            // 3. Ensure 3 Driver profiles & licenses
            var driverConfigs = new[]
            {
                new { DriverId = Driver1Id, UserId = u1.UserId, Name = "Nguyen Van Tai", IdNum = "079200000001", Phone = "0900000001", LicId = License1Id, LicNum = "B2-000001" },
                new { DriverId = Driver2Id, UserId = u2.UserId, Name = "Tran Van Binh", IdNum = "079200000002", Phone = "0900000002", LicId = License2Id, LicNum = "B2-000002" },
                new { DriverId = Driver3Id, UserId = u3.UserId, Name = "Le Hoang Nam", IdNum = "079200000003", Phone = "0900000003", LicId = License3Id, LicNum = "B2-000003" },
            };

            // 3. Ensure 3 Driver profiles
            foreach (var dc in driverConfigs)
            {
                var driver = await db.Drivers.FirstOrDefaultAsync(d => d.DriverId == dc.DriverId || d.UserId == dc.UserId || d.IdentityNumber == dc.IdNum || d.PhoneNumber == dc.Phone);
                if (driver == null)
                {
                    db.Drivers.Add(new Driver
                    {
                        DriverId = dc.DriverId,
                        UserId = dc.UserId,
                        FullName = dc.Name,
                        IdentityNumber = dc.IdNum,
                        PhoneNumber = dc.Phone,
                        DateOfBirth = new DateOnly(1990, 1, 1),
                        JoinDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
                        Status = "ACTIVE",
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    });
                }
                else
                {
                    driver.UserId = dc.UserId;
                    driver.FullName = dc.Name;
                    driver.IdentityNumber = dc.IdNum;
                    driver.PhoneNumber = dc.Phone;
                    driver.Status = "ACTIVE";
                }
            }
            await db.SaveChangesAsync();

            // 4. Ensure 3 Driver licenses
            foreach (var dc in driverConfigs)
            {
                var driver = await db.Drivers.FirstAsync(d => d.DriverId == dc.DriverId || d.UserId == dc.UserId);
                var license = await db.DriverLicenses.FirstOrDefaultAsync(l => l.DriverId == driver.DriverId);
                if (license == null)
                {
                    var existingWithNum = await db.DriverLicenses.FirstOrDefaultAsync(l => l.LicenseNumber == dc.LicNum);
                    var finalLicNum = existingWithNum == null ? dc.LicNum : $"B2-{driver.DriverId.ToString()[..6].ToUpper()}";
                    db.DriverLicenses.Add(new DriverLicense
                    {
                        LicenseId = dc.LicId,
                        DriverId = driver.DriverId,
                        LicenseNumber = finalLicNum,
                        LicenseClass = "FC",
                        IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
                        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(3)),
                        Status = "ACTIVE",
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    });
                }
                else
                {
                    license.Status = "ACTIVE";
                    license.ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(3));
                }
            }
            await db.SaveChangesAsync();

            // 5. Customer row in Customers table
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerId == SharedCustomerId);
            if (customer == null)
            {
                db.Customers.Add(new Customer
                {
                    CustomerId = SharedCustomerId,
                    CompanyName = "Công ty Cổ phần Sữa Việt Nam (Vinamilk)",
                    TaxCode = "0300588569",
                    Address = "Số 10 Tân Trào, P. Tân Phú, Q.7, TP.HCM",
                    Email = "customer01@coldchainx.com",
                    PaymentTerm = 30,
                    Status = "ACTIVE",
                    CreatedAt = DateTime.UtcNow.AddDays(-30)
                });
            }

            // 6. Ensure 6 Return-Capable Warehouses with valid coordinates
            var warehouseConfigs = new[]
            {
                new { Id = OriginWhId, Code = "WH-ORIGIN-HCM", Name = "Kho Tổng Tân Bình", Address = "10.7981, 106.6542", Type = "CROSS_DOCK" },
                new { Id = RetWhThuDucId, Code = "WH-RET-THUDUC", Name = "Kho Lạnh Thủ Đức", Address = "10.8498, 106.7715", Type = "COLD_STORAGE" },
                new { Id = RetWhBinhTanId, Code = "WH-RET-BINHTAN", Name = "Kho Lạnh Tân Tạo", Address = "10.7412, 106.5789", Type = "COLD_STORAGE" },
                new { Id = RetWhVsipId, Code = "WH-RET-BINHDUONG", Name = "Kho Lạnh VSIP 1", Address = "10.9234, 106.7012", Type = "COLD_STORAGE" },
                new { Id = RetWhLongAnId, Code = "WH-RET-LONGAN", Name = "Kho Lạnh Long Hậu", Address = "10.6234, 106.7412", Type = "COLD_STORAGE" },
                new { Id = RetWhCatLaiId, Code = "WH-RET-CATLAI", Name = "Kho Lạnh Cát Lái", Address = "10.7623, 106.7823", Type = "COLD_STORAGE" },
            };

            foreach (var wh in warehouseConfigs)
            {
                var existingWh = await db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId == wh.Id || w.WarehouseCode == wh.Code);
                if (existingWh == null)
                {
                    db.Warehouses.Add(new Warehouse
                    {
                        WarehouseId = wh.Id,
                        WarehouseCode = wh.Code,
                        WarehouseName = wh.Name,
                        Address = wh.Address,
                        WarehouseType = wh.Type,
                        MaxPallets = 5000,
                        CurrentPallets = 1200,
                        DefaultMinTemp = -25m,
                        DefaultMaxTemp = 10m,
                        Status = "ACTIVE",
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    });
                }
                else
                {
                    existingWh.Address = wh.Address;
                    existingWh.Status = "ACTIVE";
                }
            }

            // 7. Ensure Locations with valid coordinates
            var locationConfigs = new[]
            {
                new { Id = OriginLocId, Addr = "Kho Tổng Tân Bình, 102 Trường Chinh, Q. Tân Bình, TP.HCM", Lat = 10.7981m, Lon = 106.6542m },
                new { Id = Stop1PartialLocId, Addr = "Cửa hàng Vinamilk 1, 150 Nguyễn Thị Minh Khai, P.6, Q.3, TP.HCM", Lat = 10.7769m, Lon = 106.6908m },
                new { Id = Stop2PartialLocId, Addr = "Cửa hàng Vinamilk 2, 280 Hai Bà Trưng, P. Tân Định, Q.1, TP.HCM", Lat = 10.7912m, Lon = 106.6923m },
                new { Id = StopFullLocId, Addr = "Siêu thị Co.opmart Cống Quỳnh, 189C Cống Quỳnh, P. Nguyễn Cư Trinh, Q.1, TP.HCM", Lat = 10.7675m, Lon = 106.6874m },
                new { Id = StopNoShowLocId, Addr = "Bách Hóa Xanh Đinh Tiên Hoàng, 128 Đinh Tiên Hoàng, P.1, Q. Bình Thạnh, TP.HCM", Lat = 10.7951m, Lon = 106.6978m },
            };

            foreach (var loc in locationConfigs)
            {
                var existingLoc = await db.Locations.FirstOrDefaultAsync(l => l.LocationId == loc.Id);
                if (existingLoc == null)
                {
                    db.Locations.Add(new Location
                    {
                        LocationId = loc.Id,
                        CustomerId = SharedCustomerId,
                        Address = loc.Addr,
                        Latitude = loc.Lat,
                        Longitude = loc.Lon,
                        Status = "ACTIVE",
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    });
                }
                else
                {
                    existingLoc.Address = loc.Addr;
                    existingLoc.Latitude = loc.Lat;
                    existingLoc.Longitude = loc.Lon;
                    existingLoc.Status = "ACTIVE";
                }
            }

            // 8. Ensure IoT Devices attached to Vehicles
            var iotConfigs = new[]
            {
                new { Id = IotDevice1Id, VehId = Vehicle1Id, Code = "IOT-HYUNDAI-01" },
                new { Id = IotDevice2Id, VehId = Vehicle2Id, Code = "IOT-ISUZU-02" },
                new { Id = IotDevice3Id, VehId = Vehicle3Id, Code = "IOT-HINO-03" },
            };

            foreach (var iot in iotConfigs)
            {
                var device = await db.IotDevices.FirstOrDefaultAsync(d => d.DeviceId == iot.Id || d.DeviceCode == iot.Code);
                if (device == null)
                {
                    db.IotDevices.Add(new IotDevice
                    {
                        DeviceId = iot.Id,
                        VehicleId = iot.VehId,
                        DeviceCode = iot.Code,
                        Status = "STREAMING",
                        BatteryLevel = 98,
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    });
                }
                else
                {
                    device.VehicleId = iot.VehId;
                    device.Status = "STREAMING";
                }
            }

            await db.SaveChangesAsync();
        }

        private static void SeedScenarioPartial(ApplicationDbContext db, Guid driverId, Guid vehicleId, Guid iotDeviceId, ILogger logger)
        {
            logger.LogInformation("[DEV SEEDER] Seeding Scenario A: TRIP-DEV-PARTIAL-01 (2 Stops, 10 items @ Stop 1, 5 items @ Stop 2)...");
            var now = DateTime.UtcNow;

            // Trip
            var trip = new MasterTrip
            {
                TripId = TripPartialId,
                VehicleId = vehicleId,
                OriginLocationId = OriginLocId,
                DestinationLocationId = Stop2PartialLocId,
                PlannedStartTime = now.AddMinutes(-30),
                PlannedEndTime = now.AddHours(2),
                StartedAt = now.AddMinutes(-25),
                Status = "IN_TRANSIT",
                SealNumber = "SEAL-DEV-PARTIAL-01",
                TargetTemperature = 3.5m,
                TotalDistanceKm = 12.5m,
                EstimatedDurationHours = 1.5m,
                CreatedAt = now.AddMinutes(-35)
            };
            db.MasterTrips.Add(trip);

            db.TripDrivers.Add(new TripDriver
            {
                TripId = TripPartialId,
                DriverId = driverId,
                DriverRole = "PRIMARY",
                AssignedDurationHours = 2.0m,
                CreatedAt = now.AddMinutes(-35)
            });

            db.Seals.Add(new Seal
            {
                SealId = SealPartialId,
                TripId = TripPartialId,
                SealCode = "SEAL-DEV-PARTIAL-01",
                Status = "APPLIED",
                AppliedAt = now.AddMinutes(-25),
                CreatedAt = now.AddMinutes(-25)
            });

            db.TelemetryLogs.Add(new TelemetryLog
            {
                LogId = TelemetryPartialId,
                TripId = TripPartialId,
                DeviceId = iotDeviceId,
                Latitude = 10.7769m,
                Longitude = 106.6908m,
                Temperature = 3.5m,
                Timestamp = now.AddMinutes(-5)
            });

            // Stop 1 (Unhappy Stop: 10 units, 1,000,000 VND)
            db.TripStops.Add(new TripStop
            {
                StopId = StopPartial1Id,
                TripId = TripPartialId,
                LocationId = Stop1PartialLocId,
                StopSequence = 1,
                StopType = "DROPOFF",
                Status = "PLANNED",
                ActualArrivalTime = null,
                PlannedArrivalTime = now.AddMinutes(10),
                PlannedDepartureTime = now.AddMinutes(25),
                CreatedAt = now.AddMinutes(-35)
            });

            db.TransportOrders.Add(new TransportOrder
            {
                OrderId = OrderPartial1Id,
                TrackingCode = "TRK-DEV-PARTIAL-01",
                CustomerId = SharedCustomerId,
                MasterTripId = TripPartialId,
                PickupLocation = OriginLocId,
                DestLocation = Stop1PartialLocId,
                ItemName = "Sữa tươi thanh trùng 1L",
                Category = "DAIRY",
                PackingType = "Thung",
                TempCondition = "CHILLED",
                Quantity = 10,
                Status = "IN_TRANSIT",
                CreatedAt = now.AddDays(-1)
            });

            db.Quotations.Add(new Quotation
            {
                QuoteId = QuotePartial1Id,
                OrderId = OrderPartial1Id,
                BaseFreight = 900000m,
                VatAmount = 100000m,
                FinalAmount = 1000000m,
                PricingSource = "SYSTEM",
                Status = "ACCEPTED",
                CreatedAt = now.AddDays(-1)
            });

            db.WarehouseReceipts.Add(new WarehouseReceipt
            {
                ReceiptId = ReceiptPartial1Id,
                ReceiptCode = "WR-DEV-PARTIAL-01",
                OrderId = OrderPartial1Id,
                WarehouseId = OriginWhId,
                ReceiverId = SharedWarehouseWorkerUserId,
                ReceiptType = "STANDARD_INBOUND",
                DelivererName = "Vinamilk Factory Logistics",
                CreatedAt = now.AddDays(-2)
            });

            db.Lpns.Add(new Lpn
            {
                LpnId = LpnPartial1Id,
                LpnCode = "LPN-DEV-PARTIAL-01",
                OrderId = OrderPartial1Id,
                TripId = TripPartialId,
                ReceiptId = ReceiptPartial1Id,
                WarehouseId = OriginWhId,
                Quantity = 10,
                ActualWeightKg = 100.0m,
                ActualCbm = 0.35m,
                State = LpnState.SHIPPING,
                RequiredTemperature = 3.5m,
                RecordedTemperature = 3.5m,
                CreatedAt = now.AddDays(-1)
            });

            // Stop 2 (Happy Stop: 5 units, 500,000 VND)
            db.TripStops.Add(new TripStop
            {
                StopId = StopPartial2Id,
                TripId = TripPartialId,
                LocationId = Stop2PartialLocId,
                StopSequence = 2,
                StopType = "DROPOFF",
                Status = "PLANNED",
                ActualArrivalTime = null,
                PlannedArrivalTime = now.AddMinutes(40),
                PlannedDepartureTime = now.AddMinutes(55),
                CreatedAt = now.AddMinutes(-35)
            });

            db.TransportOrders.Add(new TransportOrder
            {
                OrderId = OrderPartial2Id,
                TrackingCode = "TRK-DEV-PARTIAL-02",
                CustomerId = SharedCustomerId,
                MasterTripId = TripPartialId,
                PickupLocation = OriginLocId,
                DestLocation = Stop2PartialLocId,
                ItemName = "Sữa chua uống Probi 65ml",
                Category = "DAIRY",
                PackingType = "Thung",
                TempCondition = "CHILLED",
                Quantity = 5,
                Status = "IN_TRANSIT",
                CreatedAt = now.AddDays(-1)
            });

            db.Quotations.Add(new Quotation
            {
                QuoteId = QuotePartial2Id,
                OrderId = OrderPartial2Id,
                BaseFreight = 450000m,
                VatAmount = 50000m,
                FinalAmount = 500000m,
                PricingSource = "SYSTEM",
                Status = "ACCEPTED",
                CreatedAt = now.AddDays(-1)
            });

            db.WarehouseReceipts.Add(new WarehouseReceipt
            {
                ReceiptId = ReceiptPartial2Id,
                ReceiptCode = "WR-DEV-PARTIAL-02",
                OrderId = OrderPartial2Id,
                WarehouseId = OriginWhId,
                ReceiverId = SharedWarehouseWorkerUserId,
                ReceiptType = "STANDARD_INBOUND",
                DelivererName = "Vinamilk Factory Logistics",
                CreatedAt = now.AddDays(-2)
            });

            db.Lpns.Add(new Lpn
            {
                LpnId = LpnPartial2Id,
                LpnCode = "LPN-DEV-PARTIAL-02",
                OrderId = OrderPartial2Id,
                TripId = TripPartialId,
                ReceiptId = ReceiptPartial2Id,
                WarehouseId = OriginWhId,
                Quantity = 5,
                ActualWeightKg = 40.0m,
                ActualCbm = 0.15m,
                State = LpnState.SHIPPING,
                RequiredTemperature = 4.0m,
                RecordedTemperature = 4.0m,
                CreatedAt = now.AddDays(-1)
            });
        }

        private static void SeedScenarioFullReject(ApplicationDbContext db, Guid driverId, Guid vehicleId, Guid iotDeviceId, ILogger logger)
        {
            logger.LogInformation("[DEV SEEDER] Seeding Scenario B: TRIP-DEV-FULL-01 (1 Stop, 10 items, 800,000 VND)...");
            var now = DateTime.UtcNow;

            var trip = new MasterTrip
            {
                TripId = TripFullId,
                VehicleId = vehicleId,
                OriginLocationId = OriginLocId,
                DestinationLocationId = StopFullLocId,
                PlannedStartTime = now.AddMinutes(-30),
                PlannedEndTime = now.AddHours(2),
                StartedAt = now.AddMinutes(-25),
                Status = "IN_TRANSIT",
                SealNumber = "SEAL-DEV-FULL-01",
                TargetTemperature = 4.0m,
                TotalDistanceKm = 8.5m,
                EstimatedDurationHours = 1.0m,
                CreatedAt = now.AddMinutes(-35)
            };
            db.MasterTrips.Add(trip);

            db.TripDrivers.Add(new TripDriver
            {
                TripId = TripFullId,
                DriverId = driverId,
                DriverRole = "PRIMARY",
                AssignedDurationHours = 1.5m,
                CreatedAt = now.AddMinutes(-35)
            });

            db.Seals.Add(new Seal
            {
                SealId = SealFullId,
                TripId = TripFullId,
                SealCode = "SEAL-DEV-FULL-01",
                Status = "APPLIED",
                AppliedAt = now.AddMinutes(-25),
                CreatedAt = now.AddMinutes(-25)
            });

            db.TelemetryLogs.Add(new TelemetryLog
            {
                LogId = TelemetryFullId,
                TripId = TripFullId,
                DeviceId = iotDeviceId,
                Latitude = 10.7675m,
                Longitude = 106.6874m,
                Temperature = 4.0m,
                Timestamp = now.AddMinutes(-5)
            });

            db.TripStops.Add(new TripStop
            {
                StopId = StopFullId,
                TripId = TripFullId,
                LocationId = StopFullLocId,
                StopSequence = 1,
                StopType = "DROPOFF",
                Status = "PLANNED",
                ActualArrivalTime = null,
                PlannedArrivalTime = now.AddMinutes(15),
                PlannedDepartureTime = now.AddMinutes(30),
                CreatedAt = now.AddMinutes(-35)
            });

            db.TransportOrders.Add(new TransportOrder
            {
                OrderId = OrderFullId,
                TrackingCode = "TRK-DEV-FULL-01",
                CustomerId = SharedCustomerId,
                MasterTripId = TripFullId,
                PickupLocation = OriginLocId,
                DestLocation = StopFullLocId,
                ItemName = "10 thùng sữa chua uống",
                Category = "DAIRY",
                PackingType = "Thung",
                TempCondition = "CHILLED",
                Quantity = 10,
                Status = "IN_TRANSIT",
                CreatedAt = now.AddDays(-1)
            });

            db.Quotations.Add(new Quotation
            {
                QuoteId = QuoteFullId,
                OrderId = OrderFullId,
                BaseFreight = 720000m,
                VatAmount = 80000m,
                FinalAmount = 800000m,
                PricingSource = "SYSTEM",
                Status = "ACCEPTED",
                CreatedAt = now.AddDays(-1)
            });

            db.WarehouseReceipts.Add(new WarehouseReceipt
            {
                ReceiptId = ReceiptFullId,
                ReceiptCode = "WR-DEV-FULL-01",
                OrderId = OrderFullId,
                WarehouseId = OriginWhId,
                ReceiverId = SharedWarehouseWorkerUserId,
                ReceiptType = "STANDARD_INBOUND",
                DelivererName = "Vinamilk Factory Logistics",
                CreatedAt = now.AddDays(-2)
            });

            db.Lpns.Add(new Lpn
            {
                LpnId = LpnFullId,
                LpnCode = "LPN-DEV-FULL-01",
                OrderId = OrderFullId,
                TripId = TripFullId,
                ReceiptId = ReceiptFullId,
                WarehouseId = OriginWhId,
                Quantity = 10,
                ActualWeightKg = 80.0m,
                ActualCbm = 0.30m,
                State = LpnState.SHIPPING,
                RequiredTemperature = 4.0m,
                RecordedTemperature = 4.0m,
                CreatedAt = now.AddDays(-1)
            });
        }

        private static void SeedScenarioNoShow(ApplicationDbContext db, Guid driverId, Guid vehicleId, Guid iotDeviceId, ILogger logger)
        {
            logger.LogInformation("[DEV SEEDER] Seeding Scenario C: TRIP-DEV-NOSHOW-01 (1 Stop, 10 items, 1,200,000 VND)...");
            var now = DateTime.UtcNow;

            var trip = new MasterTrip
            {
                TripId = TripNoShowId,
                VehicleId = vehicleId,
                OriginLocationId = OriginLocId,
                DestinationLocationId = StopNoShowLocId,
                PlannedStartTime = now.AddMinutes(-30),
                PlannedEndTime = now.AddHours(2),
                StartedAt = now.AddMinutes(-25),
                Status = "IN_TRANSIT",
                SealNumber = "SEAL-DEV-NOSHOW-01",
                TargetTemperature = -18.0m,
                TotalDistanceKm = 9.2m,
                EstimatedDurationHours = 1.2m,
                CreatedAt = now.AddMinutes(-35)
            };
            db.MasterTrips.Add(trip);

            db.TripDrivers.Add(new TripDriver
            {
                TripId = TripNoShowId,
                DriverId = driverId,
                DriverRole = "PRIMARY",
                AssignedDurationHours = 1.5m,
                CreatedAt = now.AddMinutes(-35)
            });

            db.Seals.Add(new Seal
            {
                SealId = SealNoShowId,
                TripId = TripNoShowId,
                SealCode = "SEAL-DEV-NOSHOW-01",
                Status = "APPLIED",
                AppliedAt = now.AddMinutes(-25),
                CreatedAt = now.AddMinutes(-25)
            });

            db.TelemetryLogs.Add(new TelemetryLog
            {
                LogId = TelemetryNoShowId,
                TripId = TripNoShowId,
                DeviceId = iotDeviceId,
                Latitude = 10.7951m,
                Longitude = 106.6978m,
                Temperature = -18.0m,
                Timestamp = now.AddMinutes(-5)
            });

            db.TripStops.Add(new TripStop
            {
                StopId = StopNoShowId,
                TripId = TripNoShowId,
                LocationId = StopNoShowLocId,
                StopSequence = 1,
                StopType = "DROPOFF",
                Status = "PLANNED",
                ActualArrivalTime = null,
                PlannedArrivalTime = now.AddMinutes(20),
                PlannedDepartureTime = now.AddMinutes(35),
                CreatedAt = now.AddMinutes(-35)
            });

            db.TransportOrders.Add(new TransportOrder
            {
                OrderId = OrderNoShowId,
                TrackingCode = "TRK-DEV-NOSHOW-01",
                CustomerId = SharedCustomerId,
                MasterTripId = TripNoShowId,
                PickupLocation = OriginLocId,
                DestLocation = StopNoShowLocId,
                ItemName = "10 thùng bơ tươi",
                Category = "FROZEN",
                PackingType = "Thung",
                TempCondition = "FROZEN",
                Quantity = 10,
                Status = "IN_TRANSIT",
                CreatedAt = now.AddDays(-1)
            });

            db.Quotations.Add(new Quotation
            {
                QuoteId = QuoteNoShowId,
                OrderId = OrderNoShowId,
                BaseFreight = 1080000m,
                VatAmount = 120000m,
                FinalAmount = 1200000m,
                PricingSource = "SYSTEM",
                Status = "ACCEPTED",
                CreatedAt = now.AddDays(-1)
            });

            db.WarehouseReceipts.Add(new WarehouseReceipt
            {
                ReceiptId = ReceiptNoShowId,
                ReceiptCode = "WR-DEV-NOSHOW-01",
                OrderId = OrderNoShowId,
                WarehouseId = OriginWhId,
                ReceiverId = SharedWarehouseWorkerUserId,
                ReceiptType = "STANDARD_INBOUND",
                DelivererName = "Vinamilk Factory Logistics",
                CreatedAt = now.AddDays(-2)
            });

            db.Lpns.Add(new Lpn
            {
                LpnId = LpnNoShowId,
                LpnCode = "LPN-DEV-NOSHOW-01",
                OrderId = OrderNoShowId,
                TripId = TripNoShowId,
                ReceiptId = ReceiptNoShowId,
                WarehouseId = OriginWhId,
                Quantity = 10,
                ActualWeightKg = 100.0m,
                ActualCbm = 0.35m,
                State = LpnState.SHIPPING,
                RequiredTemperature = -18.0m,
                RecordedTemperature = -18.0m,
                CreatedAt = now.AddDays(-1)
            });
        }

        private static void PrintScenarioSummary(ILogger logger)
        {
            logger.LogInformation("\n" +
                "=================================================================================\n" +
                "   COLDCHAINX DEV UNHAPPY SCENARIOS SEEDED SUCCESSFULLY\n" +
                "=================================================================================\n" +
                " Customer Login:     customer01@coldchainx.com / Password@123\n" +
                " Warehouse Worker:   warehouseworker01@coldchainx.com / Password@123 (Kho Tân Bình)\n" +
                "---------------------------------------------------------------------------------\n" +
                " [SCENARIO A - PARTIAL DELIVERY & SEAL CONTINUATION (2 STOPS)]\n" +
                "   Driver Login:     driver01@coldchainx.com / Password@123\n" +
                "   Driver Name:      Nguyen Van Tai (Xe: 51C-99999 - Hyundai Reefer)\n" +
                "   Trip:             TRIP-DEV-PARTIAL-01 (Id: d1000000-0000-0000-0000-000000000001)\n" +
                "   Stop 1:           TRK-DEV-PARTIAL-01 (10 thùng sữa tươi, Quotation: 1,000,000 VND)\n" +
                "                     -> Manual Action: Checkin -> CutSeal -> Reject 3 -> COD: 700k -> Seal\n" +
                "   Stop 2:           TRK-DEV-PARTIAL-02 (5 thùng sữa chua, Quotation: 500,000 VND)\n" +
                "                     -> Manual Action: Checkin -> Handover -> Final Stop -> Return Warehouse\n" +
                "---------------------------------------------------------------------------------\n" +
                " [SCENARIO B - FULL REJECTION 100% (1 STOP)]\n" +
                "   Driver Login:     driver02@coldchainx.com / Password@123\n" +
                "   Driver Name:      Tran Van Binh (Xe: 51C-88888 - Isuzu Reefer)\n" +
                "   Trip:             TRIP-DEV-FULL-01 (Id: d2000000-0000-0000-0000-000000000001)\n" +
                "   Stop 1:           TRK-DEV-FULL-01 (10 thùng sữa chua, Quotation: 800,000 VND)\n" +
                "                     -> Manual Action: Checkin -> CutSeal -> Full Reject -> Skip Pay -> Return WH\n" +
                "---------------------------------------------------------------------------------\n" +
                " [SCENARIO C - CUSTOMER NO-SHOW (1 STOP)]\n" +
                "   Driver Login:     driver03@coldchainx.com / Password@123\n" +
                "   Driver Name:      Le Hoang Nam (Xe: 51C-77777 - Hino Reefer)\n" +
                "   Trip:             TRIP-DEV-NOSHOW-01 (Id: d3000000-0000-0000-0000-000000000001)\n" +
                "   Stop 1:           TRK-DEV-NOSHOW-01 (10 thùng bơ tươi, Quotation: 1,200,000 VND)\n" +
                "                     -> Manual Action: Checkin -> Report No-Show -> ePOD NO_SHOW -> Return WH\n" +
                "=================================================================================\n");
        }
    }
}
