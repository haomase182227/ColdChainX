using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using ColdChainX.API.Controllers;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;

namespace ColdChainX.UnitTests
{
    public class DriversControllerTests
    {
        private readonly ApplicationDbContext _db;
        private readonly Mock<IDriverService> _mockDriverService;
        private readonly Mock<IFleetManagementService> _mockFleetService;

        public DriversControllerTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _db = new ApplicationDbContext(options);

            _mockDriverService = new Mock<IDriverService>();
            _mockFleetService = new Mock<IFleetManagementService>();
        }

        private DriversController CreateController(Guid? userId = null)
        {
            var controller = new DriversController(
                _mockDriverService.Object,
                _mockFleetService.Object,
                _db
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(
                userId.HasValue ? new System.Security.Claims.Claim[] { new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) } : Array.Empty<System.Security.Claims.Claim>(),
                userId.HasValue ? "TestAuth" : null));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            return controller;
        }

        [Fact]
        public async Task GetMyTrips_ValidDriverWithTrips_Returns200WithTrips()
        {
            var userId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            var tripId = Guid.NewGuid();

            _db.Drivers.Add(new Driver { DriverId = driverId, UserId = userId, FullName = "Driver A", IdentityNumber = "123", PhoneNumber = "123" });
            
            var trip = new MasterTrip { TripId = tripId, Status = "PLANNED" };
            _db.MasterTrips.Add(trip);
            _db.TripDrivers.Add(new TripDriver { TripId = tripId, DriverId = driverId });
            await _db.SaveChangesAsync();

            var controller = CreateController(userId);

            var result = await controller.GetMyTrips();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var responseData = okResult.Value as dynamic;
            Assert.NotNull(responseData);
            Assert.True((bool)responseData.GetType().GetProperty("success").GetValue(responseData, null));
            var data = (IEnumerable<object>)responseData.GetType().GetProperty("data").GetValue(responseData, null);
            Assert.Single(data);
        }

        [Fact]
        public async Task GetMyTrips_ValidDriverWithoutTrips_Returns200WithEmptyList()
        {
            var userId = Guid.NewGuid();
            var driverId = Guid.NewGuid();

            _db.Drivers.Add(new Driver { DriverId = driverId, UserId = userId, FullName = "Driver A", IdentityNumber = "123", PhoneNumber = "123" });
            await _db.SaveChangesAsync();

            var controller = CreateController(userId);

            var result = await controller.GetMyTrips();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var responseData = okResult.Value as dynamic;
            Assert.NotNull(responseData);
            var data = (IEnumerable<object>)responseData.GetType().GetProperty("data").GetValue(responseData, null);
            Assert.Empty(data);
        }

        [Fact]
        public async Task GetMyTrips_UserWithoutDriverProfile_ReturnsNotFound()
        {
            var userId = Guid.NewGuid();
            var controller = CreateController(userId);

            var result = await controller.GetMyTrips();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            var responseData = notFoundResult.Value as dynamic;
            Assert.False((bool)responseData.GetType().GetProperty("success").GetValue(responseData, null));
            Assert.Equal("Không tìm thấy hồ sơ tài xế liên kết với tài khoản này.", (string)responseData.GetType().GetProperty("message").GetValue(responseData, null));
        }

        [Fact]
        public async Task GetMyTrips_NoAuthentication_ReturnsUnauthorized()
        {
            var controller = CreateController(null); // No userId claim

            var result = await controller.GetMyTrips();

            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            var responseData = unauthorizedResult.Value as dynamic;
            Assert.False((bool)responseData.GetType().GetProperty("success").GetValue(responseData, null));
            Assert.Contains("Không tìm thấy", (string)responseData.GetType().GetProperty("message").GetValue(responseData, null));
        }

        [Fact]
        public async Task GetMyTrips_DriverACannotSeeDriverBTrips()
        {
            var userIdA = Guid.NewGuid();
            var driverIdA = Guid.NewGuid();
            var userIdB = Guid.NewGuid();
            var driverIdB = Guid.NewGuid();
            var tripIdB = Guid.NewGuid();

            _db.Drivers.Add(new Driver { DriverId = driverIdA, UserId = userIdA, FullName = "Driver A", IdentityNumber = "1", PhoneNumber = "1" });
            _db.Drivers.Add(new Driver { DriverId = driverIdB, UserId = userIdB, FullName = "Driver B", IdentityNumber = "2", PhoneNumber = "2" });
            
            var tripB = new MasterTrip { TripId = tripIdB, Status = "PLANNED" };
            _db.MasterTrips.Add(tripB);
            _db.TripDrivers.Add(new TripDriver { TripId = tripIdB, DriverId = driverIdB });
            await _db.SaveChangesAsync();

            var controller = CreateController(userIdA);

            var result = await controller.GetMyTrips();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var responseData = okResult.Value as dynamic;
            var data = (IEnumerable<object>)responseData.GetType().GetProperty("data").GetValue(responseData, null);
            Assert.Empty(data);
        }

        [Fact]
        public async Task GetDriverTrips_OldEndpoint_StillWorks()
        {
            var driverId = Guid.NewGuid();
            var tripId = Guid.NewGuid();

            _db.Drivers.Add(new Driver { DriverId = driverId, FullName = "Driver A", IdentityNumber = "1", PhoneNumber = "1" });
            var trip = new MasterTrip { TripId = tripId, Status = "PLANNED" };
            _db.MasterTrips.Add(trip);
            _db.TripDrivers.Add(new TripDriver { TripId = tripId, DriverId = driverId });
            await _db.SaveChangesAsync();

            var controller = CreateController();

            var result = await controller.GetDriverTrips(driverId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var responseData = okResult.Value as dynamic;
            var data = (IEnumerable<object>)responseData.GetType().GetProperty("data").GetValue(responseData, null);
            Assert.Single(data);
        }
    }
}
