using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class DriverAvailabilityServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly DriverAvailabilityService _service;
        private readonly Guid _driverId = Guid.NewGuid();

        public DriverAvailabilityServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _service = new DriverAvailabilityService(_db);

            _db.Drivers.Add(new Driver
            {
                DriverId = _driverId,
                FullName = "Test Driver",
                IdentityNumber = "0001",
                PhoneNumber = "0900",
                DateOfBirth = new DateOnly(1990, 1, 1),
                JoinDate = new DateOnly(2024, 1, 1),
                Status = "ACTIVE"
            });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private async Task LogHoursAsync(decimal hours, DateOnly day)
        {
            _db.DriverWorkLogs.Add(new DriverWorkLog
            {
                WorkLogId = Guid.NewGuid(),
                DriverId = _driverId,
                WorkDate = day,
                DrivingHours = hours
            });
            await _db.SaveChangesAsync();
        }

        [Fact]
        public async Task CheckAsync_AllowsWhenUnderDailyLimit()
        {
            var day = new DateOnly(2026, 6, 24); // a Wednesday
            await LogHoursAsync(6m, day);

            var result = await _service.CheckAsync(_driverId, 3m, day); // 6 + 3 = 9 <= 10

            Assert.True(result.CanAssign);
            Assert.Equal(6m, result.DayHours);
        }

        [Fact]
        public async Task CheckAsync_BlocksWhenExceedsDailyLimit()
        {
            var day = new DateOnly(2026, 6, 24);
            await LogHoursAsync(8m, day);

            var result = await _service.CheckAsync(_driverId, 3m, day); // 8 + 3 = 11 > 10

            Assert.False(result.CanAssign);
            Assert.NotNull(result.Reason);
        }

        [Fact]
        public async Task CheckAsync_BlocksWhenExceedsWeeklyLimit()
        {
            // Mon–Sun week containing 2026-06-24 is 2026-06-22 .. 2026-06-28.
            await LogHoursAsync(9m, new DateOnly(2026, 6, 22));
            await LogHoursAsync(9m, new DateOnly(2026, 6, 23));
            await LogHoursAsync(9m, new DateOnly(2026, 6, 25));
            await LogHoursAsync(9m, new DateOnly(2026, 6, 26));
            await LogHoursAsync(9m, new DateOnly(2026, 6, 27)); // week total = 45h

            // Same-day addition stays under daily limit but pushes the week over 48.
            var result = await _service.CheckAsync(_driverId, 5m, new DateOnly(2026, 6, 24)); // week 45 + 5 = 50 > 48

            Assert.False(result.CanAssign);
            Assert.Equal(45m, result.WeekHours);
        }

        [Fact]
        public async Task ReconcileStatusAsync_SetsRelaxWhenOverDailyLimit()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await LogHoursAsync(10m, today);

            var driver = await _db.Drivers.FindAsync(_driverId);
            await _service.ReconcileStatusAsync(driver!);

            Assert.Equal("RELAX", driver!.Status);
        }

        [Fact]
        public async Task ReconcileStatusAsync_ClearsRelaxWhenWindowExpired()
        {
            // Hours only in a previous calendar period → current day/week is clear.
            await LogHoursAsync(10m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30));

            var driver = await _db.Drivers.FindAsync(_driverId);
            driver!.Status = "RELAX";
            await _service.ReconcileStatusAsync(driver);

            Assert.Equal("ACTIVE", driver.Status);
        }
    }
}
