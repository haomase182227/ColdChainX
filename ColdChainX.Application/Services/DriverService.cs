using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class DriverService : IDriverService
    {
        private const string AvailableStatus = "AVAILABLE";
        private const string InactiveStatus = "INACTIVE";

        private readonly IDriverRepository _driverRepository;

        public DriverService(IDriverRepository driverRepository)
        {
            _driverRepository = driverRepository;
        }

        public async Task<ApiResponse<List<DriverDto>>> GetAllAsync()
        {
            var drivers = await _driverRepository.GetAllAsync();
            var data = drivers.Select(Map).ToList();
            return ApiResponse<List<DriverDto>>.SuccessResponse(data);
        }

        public async Task<ApiResponse<DriverDto>> GetByIdAsync(Guid id)
        {
            var driver = await _driverRepository.GetByIdAsync(id);
            if (driver == null)
                return ApiResponse<DriverDto>.Failure("Driver not found");

            return ApiResponse<DriverDto>.SuccessResponse(Map(driver));
        }

        public async Task<ApiResponse<DriverDto>> CreateAsync(DriverCreateRequest request)
        {
            var driver = new Driver
            {
                DriverId = Guid.NewGuid(),
                DateOfBirth = request.DateOfBirth,
                Status = NormalizeStatus(request.Status, AvailableStatus),
                CreatedAt = DbNow()
            };

            await _driverRepository.AddAsync(driver);
            await _driverRepository.SaveChangesAsync();

            return ApiResponse<DriverDto>.SuccessResponse(Map(driver), "Driver created successfully");
        }

        public async Task<ApiResponse<DriverDto>> UpdateAsync(Guid id, DriverUpdateRequest request)
        {
            var driver = await _driverRepository.GetByIdAsync(id);
            if (driver == null)
                return ApiResponse<DriverDto>.Failure("Driver not found");

            if (request.DateOfBirth.HasValue)
                driver.DateOfBirth = request.DateOfBirth.Value;

            if (request.Status != null)
                driver.Status = NormalizeStatus(request.Status, driver.Status ?? AvailableStatus);

            await _driverRepository.UpdateAsync(driver);
            await _driverRepository.SaveChangesAsync();

            return ApiResponse<DriverDto>.SuccessResponse(Map(driver), "Driver updated successfully");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
        {
            var driver = await _driverRepository.GetByIdAsync(id);
            if (driver == null)
                return ApiResponse<bool>.Failure("Driver not found");

            driver.Status = InactiveStatus;
            await _driverRepository.DeleteAsync(driver);
            await _driverRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Driver deleted successfully");
        }

        private static DriverDto Map(Driver driver)
        {
            return new DriverDto
            {
                DriverId = driver.DriverId,
                UserId = driver.UserId,
                Username = driver.User?.Username,
                Email = driver.User?.Email,
                FullName = driver.User?.FullName,
                DateOfBirth = driver.DateOfBirth,
                Status = driver.Status,
                CreatedAt = driver.CreatedAt,
                DriverLicenses = driver.DriverLicenses?.Select(l => new DriverLicenseDto
                {
                    LicenseId = l.LicenseId,
                    DriverId = l.DriverId,
                    LicenseNumber = l.LicenseNumber,
                    LicenseClass = l.LicenseClass,
                    IssueDate = l.IssueDate,
                    ExpiryDate = l.ExpiryDate,
                    DocumentUrl = l.DocumentUrl,
                    Status = l.Status,
                    CreatedAt = l.CreatedAt
                }).ToList() ?? new List<DriverLicenseDto>()
            };
        }

        private static string NormalizeStatus(string? value, string defaultValue)
            => string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToUpperInvariant();

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }
}