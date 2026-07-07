using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

public class FleetManagementService : IFleetManagementService
{
    private static readonly string[] RequiredVehicleDocuments =
    {
        "REGISTRATION",
        "INSURANCE",
        "CITY_PERMIT",
        "FOOD_SAFETY"
    };

    private const string DefaultDriverPassword = "@123@";

    private readonly ApplicationDbContext _db;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IFileService _fileService;

    public FleetManagementService(ApplicationDbContext db, IHubContext<NotificationHub> hubContext, IPasswordHasher<User> passwordHasher, IFileService fileService)
    {
        _db = db;
        _hubContext = hubContext;
        _passwordHasher = passwordHasher;
        _fileService = fileService;
    }

    public async Task<ApiResponse<IReadOnlyCollection<VehicleFleetResponse>>> GetVehiclesAsync()
    {
        var vehicles = await _db.Vehicles
            .Include(v => v.VehicleDocuments)
            .Where(v => v.Status != "DELETED")
            .OrderBy(v => v.TruckPlate)
            .ToListAsync();

        return ApiResponse<IReadOnlyCollection<VehicleFleetResponse>>.SuccessResponse(vehicles.Select(ToVehicleResponse).ToList());
    }

    public async Task<ApiResponse<VehicleFleetResponse>> GetVehicleByIdAsync(Guid vehicleId)
    {
        var vehicle = await _db.Vehicles
            .Include(v => v.VehicleDocuments)
            .FirstOrDefaultAsync(v => v.VehicleId == vehicleId && v.Status != "DELETED");

        return vehicle == null
            ? ApiResponse<VehicleFleetResponse>.Failure("Vehicle not found")
            : ApiResponse<VehicleFleetResponse>.SuccessResponse(ToVehicleResponse(vehicle));
    }

    public async Task<ApiResponse<VehicleFleetResponse>> CreateVehicleAsync(CreateVehicleRequest request)
    {
        var plate = NormalizeRequired(request.TruckPlate);
        if (await _db.Vehicles.AnyAsync(v => v.TruckPlate.ToUpper() == plate.ToUpper() && v.Status != "DELETED"))
            return ApiResponse<VehicleFleetResponse>.Failure("Truck plate already exists");

        // Tài xế không còn gắn trực tiếp với xe — tài xế được gán theo từng chuyến (TripDriver).

        var vehicle = new Vehicle
        {
            VehicleId = Guid.NewGuid(),
            TruckPlate = plate,
            Brand = TrimOrNull(request.Brand),
            StandardFuelLiters = request.StandardFuelLiters,
            VehicleType = NormalizeRequired(request.VehicleType),
            MaxWeight = request.MaxWeight,
            MaxCbm = request.MaxCbm,
            MinTemp = request.MinTemp,
            MaxTemp = request.MaxTemp,
            CurrentLocation = TrimOrNull(request.CurrentLocation),
            CurrentOdometer = request.CurrentOdometer,
            NextMaintenanceOdometer = request.NextMaintenanceOdometer > 0
                ? request.NextMaintenanceOdometer
                : request.CurrentOdometer + 10000,
            NextMaintenanceDate = DateOnly.FromDateTime(DateTime.Today).AddDays(365),
            WarningDaysBeforeDue = 15,
            WarningKmBeforeDue = 500.0,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now
        };
        _db.Vehicles.Add(vehicle);

        // Tạo giấy tờ kèm theo (nếu có)
        AddInlineVehicleDocument(vehicle, "REGISTRATION", request.Registration);
        AddInlineVehicleDocument(vehicle, "INSURANCE",     request.Insurance);
        AddInlineVehicleDocument(vehicle, "CITY_PERMIT",  request.CityPermit);
        AddInlineVehicleDocument(vehicle, "FOOD_SAFETY",  request.FoodSafety);

        await RefreshVehicleStatusAsync(vehicle);
        await _db.SaveChangesAsync();

        return ApiResponse<VehicleFleetResponse>.SuccessResponse(ToVehicleResponse(vehicle), "Vehicle created successfully");
    }

    public async Task<ApiResponse<bool>> SoftDeleteVehicleAsync(Guid vehicleId)
    {
        var vehicle = await _db.Vehicles.FindAsync(vehicleId);
        if (vehicle == null) return ApiResponse<bool>.Failure("Vehicle not found");

        vehicle.Status = "DELETED";
        await _db.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Vehicle deleted successfully");
    }

    public async Task<ApiResponse<ImportResultResponse>> ImportVehiclesAsync(IFormFile excelFile)
    {
        var rows = await SpreadsheetReader.ReadAsync(excelFile);
        var result = new ImportResultResponse();
        var importedVehicles = new Dictionary<string, Vehicle>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            try
            {
                var plate = NormalizeRequired(Get(row, "TruckPlate", "BienSoXe", "Bien so xe"));
                if (!importedVehicles.TryGetValue(plate, out var vehicle))
                {
                    vehicle = await _db.Vehicles
                        .Include(v => v.VehicleDocuments)
                        .FirstOrDefaultAsync(v => v.TruckPlate.ToUpper() == plate.ToUpper());
                }

                if (vehicle == null)
                {
                    vehicle = new Vehicle
                    {
                        VehicleId = Guid.NewGuid(),
                        TruckPlate = plate,
                        CreatedAt = DateTime.Now,
                        Status = "ACTIVE"
                    };
                    _db.Vehicles.Add(vehicle);
                    result.Inserted++;
                }
                else
                {
                    result.Updated++;
                }

                importedVehicles[plate] = vehicle;

                vehicle.Brand = TrimOrNull(Get(row, "Brand", "Hang xe")) ?? vehicle.Brand;
                vehicle.VehicleType = TrimOrNull(Get(row, "VehicleType", "Loai xe")) ?? vehicle.VehicleType ?? "TRUCK";
                vehicle.StandardFuelLiters = GetDecimal(row, vehicle.StandardFuelLiters, "StandardFuelLiters", "DinhMucNhienLieu");
                vehicle.MaxWeight = GetDecimal(row, vehicle.MaxWeight, "MaxWeight", "Tai trong");
                vehicle.MaxCbm = GetDecimal(row, vehicle.MaxCbm, "MaxCbm", "So khoi");
                vehicle.MinTemp = GetDecimal(row, vehicle.MinTemp, "MinTemp", "Nhiet do min");
                vehicle.MaxTemp = GetDecimal(row, vehicle.MaxTemp, "MaxTemp", "Nhiet do max");
                vehicle.CurrentLocation = TrimOrNull(Get(row, "CurrentLocation", "Vi tri")) ?? vehicle.CurrentLocation;
                vehicle.CurrentOdometer = GetDouble(row, vehicle.CurrentOdometer, "CurrentOdometer", "Odometer");
                vehicle.NextMaintenanceOdometer = GetDouble(row, vehicle.NextMaintenanceOdometer, "NextMaintenanceOdometer", "MocBaoDuongTiepTheo");

                // Tài xế không còn gắn trực tiếp với xe — gán theo từng chuyến (TripDriver).

                UpsertVehicleDocumentFromImport(vehicle, row, "REGISTRATION", "Registration", "DangKiem");
                UpsertVehicleDocumentFromImport(vehicle, row, "INSURANCE", "Insurance", "BaoHiem");
                UpsertVehicleDocumentFromImport(vehicle, row, "CITY_PERMIT", "CityPermit", "GiayPhepVaoPho");
                UpsertVehicleDocumentFromImport(vehicle, row, "FOOD_SAFETY", "FoodSafety", "AnToanThucPham");
                await RefreshVehicleStatusAsync(vehicle);
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors = result.Errors.Append(ex.Message).ToList();
            }
        }

        await _db.SaveChangesAsync();
        return ApiResponse<ImportResultResponse>.SuccessResponse(result, "Vehicles imported");
    }

    public async Task<ApiResponse<IReadOnlyCollection<DriverFleetResponse>>> GetDriversAsync()
    {
        var drivers = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Include(d => d.User)
            .Where(d => d.Status != "DELETED")
            .OrderBy(d => d.FullName)
            .ToListAsync();

        return ApiResponse<IReadOnlyCollection<DriverFleetResponse>>.SuccessResponse(drivers.Select(ToDriverResponse).ToList());
    }

    public async Task<ApiResponse<DriverFleetResponse>> GetDriverByIdAsync(Guid driverId)
    {
        var driver = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.DriverId == driverId && d.Status != "DELETED");

        return driver == null
            ? ApiResponse<DriverFleetResponse>.Failure("Driver not found")
            : ApiResponse<DriverFleetResponse>.SuccessResponse(ToDriverResponse(driver));
    }

    public async Task<ApiResponse<DriverFleetResponse>> CreateDriverAsync(CreateDriverRequest request)
    {
        var identityNumber = NormalizeRequired(request.IdentityNumber);
        if (await _db.Drivers.AnyAsync(d => d.IdentityNumber == identityNumber && d.Status != "DELETED"))
            return ApiResponse<DriverFleetResponse>.Failure("Identity number already exists");

        var email = NormalizeRequired(request.Email).ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return ApiResponse<DriverFleetResponse>.Failure("Email already in use");

        // Lấy role Driver
        var driverRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Driver");
        if (driverRole == null)
            return ApiResponse<DriverFleetResponse>.Failure("Driver role not found in the system");

        // Tạo tài khoản User cho tài xế
        var user = new User
        {
            UserId = Guid.NewGuid(),
            Username = email,
            FullName = NormalizeRequired(request.FullName),
            Email = email,
            RoleId = driverRole.RoleId,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, DefaultDriverPassword);
        _db.Users.Add(user);

        var driver = new Driver
        {
            DriverId = Guid.NewGuid(),
            UserId = user.UserId,
            User = user,
            FullName = NormalizeRequired(request.FullName),
            IdentityNumber = identityNumber,
            PhoneNumber = NormalizeRequired(request.PhoneNumber),
            DateOfBirth = request.DateOfBirth,
            JoinDate = request.JoinDate,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now
        };
        _db.Drivers.Add(driver);

        // Tạo bằng lái kèm theo (nếu có)
        if (request.License != null)
        {
            var license = new DriverLicense
            {
                LicenseId = Guid.NewGuid(),
                DriverId = driver.DriverId,
                LicenseNumber = NormalizeRequired(request.License.LicenseNumber),
                LicenseClass = NormalizeRequired(request.License.LicenseClass).ToUpperInvariant(),
                IssueDate = request.License.IssueDate,
                ExpiryDate = request.License.ExpiryDate,
                Status = "ACTIVE",
                CreatedAt = DateTime.Now
            };
            _db.DriverLicenses.Add(license);
            driver.DriverLicenses.Add(license);
        }

        await RefreshDriverStatusAsync(driver);
        await _db.SaveChangesAsync();

        return ApiResponse<DriverFleetResponse>.SuccessResponse(ToDriverResponse(driver), "Driver created successfully. Default password: @123@");
    }

    public async Task<ApiResponse<DriverFleetResponse>> UpdateDriverAsync(Guid driverId, UpdateDriverRequest request)
    {
        var driver = await _db.Drivers
            .Include(d => d.DriverLicenses)
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.DriverId == driverId && d.Status != "DELETED");

        if (driver == null)
            return ApiResponse<DriverFleetResponse>.Failure("Driver not found");

        if (!string.IsNullOrWhiteSpace(request.IdentityNumber))
        {
            var identityNumber = NormalizeRequired(request.IdentityNumber);
            var identityExists = await _db.Drivers.AnyAsync(d =>
                d.DriverId != driverId
                && d.IdentityNumber == identityNumber
                && d.Status != "DELETED");

            if (identityExists)
                return ApiResponse<DriverFleetResponse>.Failure("Identity number already exists");

            driver.IdentityNumber = identityNumber;
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            driver.FullName = NormalizeRequired(request.FullName);
            if (driver.User != null)
                driver.User.FullName = driver.FullName;
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            driver.PhoneNumber = NormalizeRequired(request.PhoneNumber);

        if (request.DateOfBirth.HasValue)
            driver.DateOfBirth = request.DateOfBirth.Value;

        if (request.JoinDate.HasValue)
            driver.JoinDate = request.JoinDate.Value;

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = NormalizeRequired(request.Email).ToLowerInvariant();
            var emailExists = await _db.Users.AnyAsync(u =>
                u.UserId != driver.UserId
                && u.Email != null
                && u.Email.ToLower() == email);

            if (emailExists)
                return ApiResponse<DriverFleetResponse>.Failure("Email already in use");

            if (driver.User == null)
            {
                var driverRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Driver");
                if (driverRole == null)
                    return ApiResponse<DriverFleetResponse>.Failure("Driver role not found in the system");

                driver.User = new User
                {
                    UserId = Guid.NewGuid(),
                    Username = email,
                    FullName = driver.FullName,
                    Email = email,
                    RoleId = driverRole.RoleId,
                    Status = "ACTIVE",
                    CreatedAt = DateTime.Now
                };
                driver.User.PasswordHash = _passwordHasher.HashPassword(driver.User, DefaultDriverPassword);
                driver.UserId = driver.User.UserId;
                _db.Users.Add(driver.User);
            }
            else
            {
                driver.User.Email = email;
                driver.User.Username = email;
                driver.User.UpdatedAt = DateTime.Now;
            }
        }

        await RefreshDriverStatusAsync(driver);

        if (!string.IsNullOrWhiteSpace(request.Status))
            driver.Status = NormalizeRequired(request.Status).ToUpperInvariant();

        await _db.SaveChangesAsync();
        return ApiResponse<DriverFleetResponse>.SuccessResponse(ToDriverResponse(driver), "Driver updated successfully");
    }

    public async Task<ApiResponse<bool>> SoftDeleteDriverAsync(Guid driverId)
    {
        var driver = await _db.Drivers.FindAsync(driverId);
        if (driver == null) return ApiResponse<bool>.Failure("Driver not found");

        driver.Status = "DELETED";
        await _db.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Driver deleted successfully");
    }

    public async Task<ApiResponse<ImportResultResponse>> ImportDriversAsync(IFormFile excelFile)
    {
        var rows = await SpreadsheetReader.ReadAsync(excelFile);
        var result = new ImportResultResponse();
        var importedDrivers = new Dictionary<string, Driver>(StringComparer.OrdinalIgnoreCase);
        var licenseOwnerByNumber = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 1;

        foreach (var row in rows)
        {
            rowNumber++;
            try
            {
                var identityNumber = NormalizeRequired(Get(row, "IdentityNumber", "CCCD", "Can cuoc"));
                var licenseNumber = NormalizeRequired(Get(row, "LicenseNumber", "So bang"));
                if (licenseOwnerByNumber.TryGetValue(licenseNumber, out var existingIdentity)
                    && !string.Equals(existingIdentity, identityNumber, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Row {rowNumber}: License number '{licenseNumber}' is duplicated for another driver '{existingIdentity}'");
                }

                licenseOwnerByNumber[licenseNumber] = identityNumber;

                if (!importedDrivers.TryGetValue(identityNumber, out var driver))
                {
                    driver = await _db.Drivers
                        .Include(d => d.DriverLicenses)
                        .FirstOrDefaultAsync(d => d.IdentityNumber == identityNumber);
                }

                if (driver == null)
                {
                    driver = new Driver
                    {
                        DriverId = Guid.NewGuid(),
                        IdentityNumber = identityNumber,
                        CreatedAt = DateTime.Now,
                        Status = "ACTIVE"
                    };
                    _db.Drivers.Add(driver);
                    result.Inserted++;

                    // Tự động tạo User nếu có cột Email
                    var emailRaw = TrimOrNull(Get(row, "Email", "email"));
                    if (!string.IsNullOrWhiteSpace(emailRaw))
                    {
                        var emailNorm = emailRaw.ToLowerInvariant();
                        var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == emailNorm);
                        if (existingUser != null)
                        {
                            // Liên kết user đã có nếu chưa linked
                            driver.UserId = existingUser.UserId;
                        }
                        else
                        {
                            var driverRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Driver");
                            if (driverRole != null)
                            {
                                var newUser = new User
                                {
                                    UserId = Guid.NewGuid(),
                                    Username = emailNorm,
                                    FullName = TrimOrNull(Get(row, "FullName", "Ho ten")) ?? identityNumber,
                                    Email = emailNorm,
                                    RoleId = driverRole.RoleId,
                                    Status = "ACTIVE",
                                    CreatedAt = DateTime.Now
                                };
                                newUser.PasswordHash = _passwordHasher.HashPassword(newUser, DefaultDriverPassword);
                                _db.Users.Add(newUser);
                                driver.UserId = newUser.UserId;
                                driver.User = newUser;
                            }
                            else
                            {
                                result.Errors = result.Errors.Append($"Row {rowNumber}: Driver role not found — user not created for '{emailNorm}'").ToList();
                            }
                        }
                    }
                }
                else
                {
                    result.Updated++;
                }

                importedDrivers[identityNumber] = driver;

                driver.FullName = NormalizeRequired(Get(row, "FullName", "Ho ten"));
                driver.PhoneNumber = NormalizeRequired(Get(row, "PhoneNumber", "So dien thoai"));
                driver.DateOfBirth = GetDate(row, DateOnly.FromDateTime(DateTime.Today), "DateOfBirth", "Ngay sinh");
                driver.JoinDate = GetDate(row, DateOnly.FromDateTime(DateTime.Today), "JoinDate", "Ngay vao lam");

                await UpsertDriverLicenseFromImportAsync(driver, row);
                await RefreshDriverStatusAsync(driver);
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors = result.Errors.Append(ex.Message.StartsWith("Row ", StringComparison.OrdinalIgnoreCase)
                    ? ex.Message
                    : $"Row {rowNumber}: {ex.Message}").ToList();
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return ApiResponse<ImportResultResponse>.Failure($"Import drivers failed while saving. Detail: {ex.InnerException?.Message ?? ex.Message}");
        }

        return ApiResponse<ImportResultResponse>.SuccessResponse(result, "Drivers imported");
    }

    public async Task<ApiResponse<IReadOnlyCollection<VehicleDocumentResponse>>> GetVehicleDocumentsAsync(Guid? vehicleId)
    {
        var query = _db.VehicleDocuments.AsQueryable();
        if (vehicleId.HasValue)
            query = query.Where(d => d.VehicleId == vehicleId.Value);

        var docs = await query.OrderBy(d => d.DocumentType).ToListAsync();
        return ApiResponse<IReadOnlyCollection<VehicleDocumentResponse>>.SuccessResponse(
            docs.Select(ToVehicleDocumentResponse).ToList());
    }

    public async Task<ApiResponse<VehicleDocumentResponse>> GetVehicleDocumentByIdAsync(Guid docId)
    {
        var doc = await _db.VehicleDocuments.FirstOrDefaultAsync(d => d.DocId == docId);
        return doc == null
            ? ApiResponse<VehicleDocumentResponse>.Failure("Vehicle document not found")
            : ApiResponse<VehicleDocumentResponse>.SuccessResponse(ToVehicleDocumentResponse(doc));
    }

    public async Task<ApiResponse<VehicleDocumentResponse>> CreateVehicleDocumentAsync(Guid vehicleId, CreateVehicleDocumentRequest request)
    {
        var vehicle = await _db.Vehicles.Include(v => v.VehicleDocuments).FirstOrDefaultAsync(v => v.VehicleId == vehicleId);
        if (vehicle == null) return ApiResponse<VehicleDocumentResponse>.Failure("Vehicle not found");

        var doc = new VehicleDocument
        {
            DocId = Guid.NewGuid(),
            VehicleId = vehicleId,
            DocumentType = NormalizeDocumentType(request.DocumentType),
            DocumentNumber = NormalizeRequired(request.DocumentNumber),
            Issuer = TrimOrNull(request.Issuer),
            IssueDate = request.IssueDate,
            ExpireDate = request.ExpireDate,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now
        };

        _db.VehicleDocuments.Add(doc);
        vehicle.VehicleDocuments.Add(doc);
        await RefreshVehicleStatusAsync(vehicle);
        await _db.SaveChangesAsync();
        return ApiResponse<VehicleDocumentResponse>.SuccessResponse(ToVehicleDocumentResponse(doc), "Vehicle document created successfully");
    }

    public async Task<ApiResponse<VehicleDocumentResponse>> UpdateVehicleDocumentAsync(Guid docId, UpdateVehicleDocumentRequest request)
    {
        var doc = await _db.VehicleDocuments
            .Include(d => d.Vehicle)
            .ThenInclude(v => v!.VehicleDocuments)
            .FirstOrDefaultAsync(d => d.DocId == docId);

        if (doc == null) return ApiResponse<VehicleDocumentResponse>.Failure("Vehicle document not found");

        doc.DocumentType = NormalizeDocumentType(request.DocumentType);
        doc.DocumentNumber = NormalizeRequired(request.DocumentNumber);
        doc.Issuer = TrimOrNull(request.Issuer);
        doc.IssueDate = request.IssueDate;
        doc.ExpireDate = request.ExpireDate;
        doc.Status = "ACTIVE";

        if (doc.Vehicle != null) await RefreshVehicleStatusAsync(doc.Vehicle);
        await _db.SaveChangesAsync();
        return ApiResponse<VehicleDocumentResponse>.SuccessResponse(ToVehicleDocumentResponse(doc), "Vehicle document updated successfully");
    }

    public async Task<ApiResponse<bool>> DeleteVehicleDocumentAsync(Guid docId)
    {
        var doc = await _db.VehicleDocuments
            .Include(d => d.Vehicle)
            .ThenInclude(v => v!.VehicleDocuments)
            .FirstOrDefaultAsync(d => d.DocId == docId);

        if (doc == null) return ApiResponse<bool>.Failure("Vehicle document not found");

        var vehicle = doc.Vehicle;
        _db.VehicleDocuments.Remove(doc);
        if (vehicle != null)
        {
            vehicle.VehicleDocuments.Remove(doc);
            await RefreshVehicleStatusAsync(vehicle);
        }

        await _db.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Vehicle document deleted successfully");
    }

    public async Task<ApiResponse<ImportResultResponse>> ImportVehicleDocumentsAsync(IFormFile excelFile)
    {
        var rows = await SpreadsheetReader.ReadAsync(excelFile);
        var result = new ImportResultResponse();
        var touchedVehicles = new HashSet<Guid>();

        foreach (var row in rows)
        {
            try
            {
                var plate = NormalizeRequired(Get(row, "Bien so xe", "TruckPlate"));
                var vehicle = await _db.Vehicles
                    .Include(v => v.VehicleDocuments)
                    .FirstOrDefaultAsync(v => v.TruckPlate.ToUpper() == plate.ToUpper());
                if (vehicle == null) throw new InvalidOperationException($"Vehicle not found: {plate}");

                var type = NormalizeDocumentType(Get(row, "Loai giay to", "DocumentType"));
                var doc = vehicle.VehicleDocuments.FirstOrDefault(d => d.DocumentType == type);
                if (doc == null)
                {
                    doc = new VehicleDocument { DocId = Guid.NewGuid(), VehicleId = vehicle.VehicleId, DocumentType = type, CreatedAt = DateTime.Now };
                    _db.VehicleDocuments.Add(doc);
                    result.Inserted++;
                }
                else
                {
                    result.Updated++;
                }

                doc.DocumentNumber = NormalizeRequired(Get(row, "So giay", "DocumentNumber"));
                doc.IssueDate = GetDate(row, DateOnly.FromDateTime(DateTime.Today), "Ngay cap", "IssueDate");
                doc.ExpireDate = GetNullableDate(row, "Ngay het han", "ExpireDate");
                doc.Status = "ACTIVE";
                touchedVehicles.Add(vehicle.VehicleId);
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors = result.Errors.Append(ex.Message).ToList();
            }
        }

        foreach (var vehicleId in touchedVehicles)
        {
            var vehicle = await _db.Vehicles.Include(v => v.VehicleDocuments).FirstAsync(v => v.VehicleId == vehicleId);
            await RefreshVehicleStatusAsync(vehicle);
        }

        await _db.SaveChangesAsync();
        return ApiResponse<ImportResultResponse>.SuccessResponse(result, "Vehicle documents imported");
    }

    public async Task<ApiResponse<IReadOnlyCollection<DriverLicenseResponse>>> GetDriverLicensesAsync(Guid? driverId)
    {
        var query = _db.DriverLicenses.AsQueryable();
        if (driverId.HasValue)
            query = query.Where(l => l.DriverId == driverId.Value);

        var licenses = await query.OrderBy(l => l.LicenseClass).ToListAsync();
        return ApiResponse<IReadOnlyCollection<DriverLicenseResponse>>.SuccessResponse(
            licenses.Select(ToDriverLicenseResponse).ToList());
    }

    public async Task<ApiResponse<DriverLicenseResponse>> GetDriverLicenseByIdAsync(Guid licenseId)
    {
        var license = await _db.DriverLicenses.FirstOrDefaultAsync(l => l.LicenseId == licenseId);
        return license == null
            ? ApiResponse<DriverLicenseResponse>.Failure("Driver license not found")
            : ApiResponse<DriverLicenseResponse>.SuccessResponse(ToDriverLicenseResponse(license));
    }

    public async Task<ApiResponse<DriverLicenseResponse>> CreateDriverLicenseAsync(Guid driverId, CreateDriverLicenseRequest request)
    {
        var driver = await _db.Drivers.Include(d => d.DriverLicenses).FirstOrDefaultAsync(d => d.DriverId == driverId);
        if (driver == null) return ApiResponse<DriverLicenseResponse>.Failure("Driver not found");

        var license = new DriverLicense
        {
            LicenseId = Guid.NewGuid(),
            DriverId = driverId,
            LicenseNumber = NormalizeRequired(request.LicenseNumber),
            LicenseClass = NormalizeRequired(request.LicenseClass).ToUpperInvariant(),
            IssueDate = request.IssueDate,
            ExpiryDate = request.ExpiryDate,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now
        };

        _db.DriverLicenses.Add(license);
        driver.DriverLicenses.Add(license);
        await RefreshDriverStatusAsync(driver);
        await _db.SaveChangesAsync();
        return ApiResponse<DriverLicenseResponse>.SuccessResponse(ToDriverLicenseResponse(license), "Driver license created successfully");
    }

    public async Task<ApiResponse<DriverLicenseResponse>> UpdateDriverLicenseAsync(Guid licenseId, UpdateDriverLicenseRequest request)
    {
        var license = await _db.DriverLicenses
            .Include(l => l.Driver)
            .ThenInclude(d => d!.DriverLicenses)
            .FirstOrDefaultAsync(l => l.LicenseId == licenseId);

        if (license == null) return ApiResponse<DriverLicenseResponse>.Failure("Driver license not found");

        license.LicenseNumber = NormalizeRequired(request.LicenseNumber);
        license.LicenseClass = NormalizeRequired(request.LicenseClass).ToUpperInvariant();
        license.IssueDate = request.IssueDate;
        license.ExpiryDate = request.ExpiryDate;
        license.Status = "ACTIVE";

        if (license.Driver != null) await RefreshDriverStatusAsync(license.Driver);
        await _db.SaveChangesAsync();
        return ApiResponse<DriverLicenseResponse>.SuccessResponse(ToDriverLicenseResponse(license), "Driver license updated successfully");
    }

    public async Task<ApiResponse<bool>> DeleteDriverLicenseAsync(Guid licenseId)
    {
        var license = await _db.DriverLicenses
            .Include(l => l.Driver)
            .ThenInclude(d => d!.DriverLicenses)
            .FirstOrDefaultAsync(l => l.LicenseId == licenseId);

        if (license == null) return ApiResponse<bool>.Failure("Driver license not found");

        var driver = license.Driver;
        _db.DriverLicenses.Remove(license);
        if (driver != null)
        {
            driver.DriverLicenses.Remove(license);
            await RefreshDriverStatusAsync(driver);
        }

        await _db.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Driver license deleted successfully");
    }

    public async Task<ApiResponse<ImportResultResponse>> ImportDriverLicensesAsync(IFormFile excelFile)
    {
        var rows = await SpreadsheetReader.ReadAsync(excelFile);
        var result = new ImportResultResponse();
        var touchedDrivers = new HashSet<Guid>();
        var licenseOwnerByNumber = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 1;

        foreach (var row in rows)
        {
            rowNumber++;
            try
            {
                var identity = NormalizeRequired(Get(row, "CCCD", "IdentityNumber", "Can cuoc"));
                var licenseNumber = NormalizeRequired(Get(row, "LicenseNumber", "So bang"));
                if (licenseOwnerByNumber.TryGetValue(licenseNumber, out var existingIdentity)
                    && !string.Equals(existingIdentity, identity, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Row {rowNumber}: License number '{licenseNumber}' is duplicated for another driver '{existingIdentity}'");
                }

                licenseOwnerByNumber[licenseNumber] = identity;

                var driver = await _db.Drivers
                    .Include(d => d.DriverLicenses)
                    .FirstOrDefaultAsync(d => d.IdentityNumber == identity);
                if (driver == null) throw new InvalidOperationException($"Driver not found: {identity}");

                await UpsertDriverLicenseFromImportAsync(driver, row);
                touchedDrivers.Add(driver.DriverId);
                result.Updated++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors = result.Errors.Append(ex.Message.StartsWith("Row ", StringComparison.OrdinalIgnoreCase)
                    ? ex.Message
                    : $"Row {rowNumber}: {ex.Message}").ToList();
            }
        }

        foreach (var driverId in touchedDrivers)
        {
            var driver = await _db.Drivers.Include(d => d.DriverLicenses).FirstAsync(d => d.DriverId == driverId);
            await RefreshDriverStatusAsync(driver);
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return ApiResponse<ImportResultResponse>.Failure($"Import driver licenses failed while saving. Detail: {ex.InnerException?.Message ?? ex.Message}");
        }

        return ApiResponse<ImportResultResponse>.SuccessResponse(result, "Driver licenses imported");
    }

    public async Task<ApiResponse<VehicleFleetResponse>> SyncOdometerAsync(string truckPlate, SyncOdometerRequest request, Guid? updatedBy = null)
    {
        var plate = NormalizeRequired(truckPlate);
        var vehicle = await _db.Vehicles
            .Include(v => v.VehicleDocuments)
            .FirstOrDefaultAsync(v => v.TruckPlate.ToUpper() == plate.ToUpper() && v.Status != "DELETED");

        if (vehicle == null) return ApiResponse<VehicleFleetResponse>.Failure("Vehicle not found");

        vehicle.CurrentOdometer = request.Odometer;
        vehicle.CurrentLocation = TrimOrNull(request.LocationText);

        var odometerLog = new VehicleOdometerLog
        {
            LogId = Guid.NewGuid(),
            VehicleId = vehicle.VehicleId,
            OdometerValue = request.Odometer,
            LocationText = TrimOrNull(request.LocationText),
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "ROUTINE_SYNC" : TrimOrNull(request.Reason),
            UpdatedBy = updatedBy,
            CreatedAt = DateTime.Now
        };
        _db.VehicleOdometerLogs.Add(odometerLog);

        if (vehicle.Status == "ACTIVE" && vehicle.CurrentOdometer >= vehicle.NextMaintenanceOdometer)
        {
            var message = $"Xe {vehicle.TruckPlate} đã chạy {vehicle.CurrentOdometer:0.#} KM, vượt mốc bảo dưỡng định kỳ.";
            Guid? driverUserId = null; // Xe không còn gắn trực tiếp tài xế — chỉ thông báo cho Admin/Dispatcher.
            await AddNotificationsAsync(
                new[] { "Admin", "Dispatcher" }, driverUserId,
                "NOTI_MAINTENANCE_ODOMETER",
                new { TruckPlate = vehicle.TruckPlate, CurrentOdometer = vehicle.CurrentOdometer },
                message);

            await _hubContext.Clients.Groups("Group_Admin", "Group_Dispatcher")
                .SendAsync("MaintenanceOdometerWarning", new
                {
                    vehicle.VehicleId,
                    vehicle.TruckPlate,
                    vehicle.CurrentOdometer,
                    vehicle.NextMaintenanceOdometer,
                    Message = message
                });
                
            if (driverUserId.HasValue)
                await _hubContext.Clients.User(driverUserId.Value.ToString()).SendAsync("MaintenanceOdometerWarning", new
                {
                    vehicle.VehicleId,
                    vehicle.TruckPlate,
                    vehicle.CurrentOdometer,
                    vehicle.NextMaintenanceOdometer,
                    Message = message
                });
        }

        await _db.SaveChangesAsync();
        return ApiResponse<VehicleFleetResponse>.SuccessResponse(ToVehicleResponse(vehicle), "Odometer synced successfully");
    }

    public async Task<ApiResponse<MaintenanceTicketResponse>> CreateMaintenanceTicketAsync(Guid vehicleId, CreateMaintenanceTicketRequest request, Guid createdBy)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == vehicleId && v.Status != "DELETED");
        if (vehicle == null) return ApiResponse<MaintenanceTicketResponse>.Failure("Vehicle not found");
        if (!await _db.Users.AnyAsync(u => u.UserId == createdBy)) return ApiResponse<MaintenanceTicketResponse>.Failure("CreatedBy user not found");

        var ticket = new MaintenanceTicket
        {
            TicketId = Guid.NewGuid(),
            TicketCode = $"MT-{DateTime.Now:yyyyMMddHHmmss}",
            VehicleId = vehicle.VehicleId,
            MaintenanceType = NormalizeRequired(request.MaintenanceType),
            TriggeredAtOdometer = vehicle.CurrentOdometer,
            GarageName = NormalizeRequired(request.GarageName),
            Description = NormalizeRequired(request.Description),
            IssueDate = DateOnly.FromDateTime(DateTime.Today),
            Status = "OPEN",
            CreatedBy = createdBy,
            CreatedAt = DateTime.Now
        };

        vehicle.Status = "MAINTENANCE";
        _db.MaintenanceTickets.Add(ticket);
        await _db.SaveChangesAsync();

        return ApiResponse<MaintenanceTicketResponse>.SuccessResponse(ToMaintenanceTicketResponse(ticket), "Maintenance ticket created successfully");
    }

    public async Task<ApiResponse<MaintenanceTicketResponse>> CompleteMaintenanceTicketAsync(Guid ticketId, CompleteMaintenanceTicketRequest request)
    {
        var ticket = await _db.MaintenanceTickets
            .Include(t => t.Vehicle)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);
        if (ticket == null) return ApiResponse<MaintenanceTicketResponse>.Failure("Maintenance ticket not found");

        ticket.Status = "RESOLVED";
        ticket.Cost = request.Cost;
        ticket.CompletionDate = request.CompletionDate;

        if (ticket.Vehicle != null)
        {
            var odometerInterval = await GetSystemConfigDoubleAsync("MaintenanceIntervalOdometer", 10000.0);
            var daysInterval = await GetSystemConfigIntAsync("MaintenanceIntervalDays", 365);

            ticket.Vehicle.NextMaintenanceOdometer = ticket.Vehicle.CurrentOdometer + odometerInterval;
            ticket.Vehicle.NextMaintenanceDate = DateOnly.FromDateTime(DateTime.Today).AddDays(daysInterval);

            var hasOtherActiveTickets = await _db.MaintenanceTickets.AnyAsync(t =>
                t.VehicleId == ticket.VehicleId &&
                t.TicketId != ticket.TicketId &&
                (t.Status == "OPEN" || t.Status == "IN_PROGRESS"));

            if (!hasOtherActiveTickets)
            {
                ticket.Vehicle.Status = "ACTIVE";
            }
        }

        await _db.SaveChangesAsync();
        return ApiResponse<MaintenanceTicketResponse>.SuccessResponse(ToMaintenanceTicketResponse(ticket), "Maintenance ticket completed successfully");
    }

    public async Task RunComplianceScanAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFleetNotificationTemplatesAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var docs = await _db.VehicleDocuments
            .Include(d => d.Vehicle)
            .Where(d => d.Status == "ACTIVE" && d.ExpireDate != null)
            .ToListAsync(cancellationToken);

        foreach (var doc in docs)
        {
            var days = doc.ExpireDate!.Value.DayNumber - today.DayNumber;
            if (doc.Vehicle == null) continue;

            if (days <= 0)
            {
                doc.Status = "EXPIRED";
                doc.Vehicle.Status = "SUSPENDED_DOCS";
                Guid? driverUserId = null; // Xe không còn gắn trực tiếp tài xế.
                await AddNotificationsAsync(
                    new[] { "Admin", "Dispatcher" }, driverUserId,
                    "NOTI_FLEET_DOC_EXPIRED",
                    new { doc.Vehicle.TruckPlate, doc.DocumentType, ExpireDate = doc.ExpireDate.Value.ToString("dd/MM/yyyy") },
                    $"Chứng từ {doc.DocumentType} của xe {doc.Vehicle.TruckPlate} đã hết hạn.",
                    cancellationToken);

                await _hubContext.Clients.Groups("Group_Admin", "Group_Dispatcher")
                    .SendAsync("VehicleDocumentExpired", new { doc.VehicleId, doc.Vehicle.TruckPlate, doc.DocumentType, doc.ExpireDate }, cancellationToken);
                if (driverUserId.HasValue)
                    await _hubContext.Clients.User(driverUserId.Value.ToString()).SendAsync("VehicleDocumentExpired", new { doc.VehicleId, doc.Vehicle.TruckPlate, doc.DocumentType, doc.ExpireDate }, cancellationToken);
            }
            else if (days <= 15)
            {
                Guid? driverUserId = null; // Xe không còn gắn trực tiếp tài xế.
                await AddNotificationsAsync(
                    new[] { "Admin", "Dispatcher" }, driverUserId,
                    "NOTI_FLEET_DOC_EXPIRING",
                    new { doc.Vehicle.TruckPlate, doc.DocumentType, Days = days, ExpireDate = doc.ExpireDate.Value.ToString("dd/MM/yyyy") },
                    $"Cảnh báo: Chứng từ {doc.DocumentType} của xe {doc.Vehicle.TruckPlate} sẽ hết hạn sau {days} ngày nữa.",
                    cancellationToken);

                await _hubContext.Clients.Groups("Group_Admin", "Group_Dispatcher")
                    .SendAsync("VehicleDocumentExpiring", new { doc.VehicleId, doc.Vehicle.TruckPlate, doc.DocumentType, Days = days, doc.ExpireDate }, cancellationToken);
                if (driverUserId.HasValue)
                    await _hubContext.Clients.User(driverUserId.Value.ToString()).SendAsync("VehicleDocumentExpiring", new { doc.VehicleId, doc.Vehicle.TruckPlate, doc.DocumentType, Days = days, doc.ExpireDate }, cancellationToken);
            }
        }

        var licenses = await _db.DriverLicenses
            .Include(l => l.Driver)
            .Where(l => l.Status == "ACTIVE")
            .ToListAsync(cancellationToken);

        foreach (var license in licenses)
        {
            var days = license.ExpiryDate.DayNumber - today.DayNumber;
            if (license.Driver == null) continue;

            if (days <= 0)
            {
                license.Status = "EXPIRED";
                license.Driver.Status = "SUSPENDED_DOCS";
                var driverUserId = license.Driver.UserId;
                await AddNotificationsAsync(
                    new[] { "Admin", "Dispatcher" }, driverUserId,
                    "NOTI_DRIVER_LICENSE_EXPIRED",
                    new { license.Driver.FullName, license.LicenseClass, ExpiryDate = license.ExpiryDate.ToString("dd/MM/yyyy") },
                    $"Bằng lái hạng {license.LicenseClass} của tài xế {license.Driver.FullName} đã hết hạn.",
                    cancellationToken);

                await _hubContext.Clients.Groups("Group_Admin", "Group_Dispatcher")
                    .SendAsync("DriverLicenseExpired", new { license.LicenseId, license.DriverId, license.Driver.FullName, license.LicenseClass, license.ExpiryDate }, cancellationToken);
                if (driverUserId.HasValue)
                    await _hubContext.Clients.User(driverUserId.Value.ToString()).SendAsync("DriverLicenseExpired", new { license.LicenseId, license.DriverId, license.Driver.FullName, license.LicenseClass, license.ExpiryDate }, cancellationToken);
            }
            else if (days <= 15)
            {
                var driverUserId = license.Driver.UserId;
                await AddNotificationsAsync(
                    new[] { "Admin", "Dispatcher" }, driverUserId,
                    "NOTI_DRIVER_LICENSE_EXPIRING",
                    new { license.Driver.FullName, license.LicenseClass, Days = days, ExpiryDate = license.ExpiryDate.ToString("dd/MM/yyyy") },
                    $"Cảnh báo: Bằng lái hạng {license.LicenseClass} của tài xế {license.Driver.FullName} sẽ hết hạn sau {days} ngày nữa.",
                    cancellationToken);

                await _hubContext.Clients.Groups("Group_Admin", "Group_Dispatcher")
                    .SendAsync("DriverLicenseExpiring", new { license.LicenseId, license.DriverId, license.Driver.FullName, license.LicenseClass, Days = days, license.ExpiryDate }, cancellationToken);
                if (driverUserId.HasValue)
                    await _hubContext.Clients.User(driverUserId.Value.ToString()).SendAsync("DriverLicenseExpiring", new { license.LicenseId, license.DriverId, license.Driver.FullName, license.LicenseClass, Days = days, license.ExpiryDate }, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private void UpsertVehicleDocumentFromImport(Vehicle vehicle, IReadOnlyDictionary<string, string> row, string documentType, string englishPrefix, string vietnamesePrefix)
    {
        var number = TrimOrNull(Get(row, $"{englishPrefix}Number", $"{vietnamesePrefix}So", $"{documentType}_NUMBER"));
        var issueDate = GetNullableDate(row, $"{englishPrefix}IssueDate", $"{vietnamesePrefix}NgayCap", $"{documentType}_ISSUE_DATE");
        var expireDate = GetNullableDate(row, $"{englishPrefix}ExpireDate", $"{vietnamesePrefix}NgayHetHan", $"{documentType}_EXPIRE_DATE");

        if (number == null && issueDate == null && expireDate == null) return;

        var doc = vehicle.VehicleDocuments.FirstOrDefault(d => d.DocumentType == documentType);
        if (doc == null)
        {
            doc = new VehicleDocument
            {
                DocId = Guid.NewGuid(),
                VehicleId = vehicle.VehicleId,
                DocumentType = documentType,
                CreatedAt = DateTime.Now
            };
            _db.VehicleDocuments.Add(doc);
            vehicle.VehicleDocuments.Add(doc);
        }

        doc.DocumentNumber = number ?? doc.DocumentNumber ?? $"{documentType}-{vehicle.TruckPlate}";
        doc.IssueDate = issueDate ?? doc.IssueDate;
        doc.ExpireDate = expireDate;
        doc.Status = "ACTIVE";
    }

    private void AddInlineVehicleDocument(Vehicle vehicle, string documentType, InlineVehicleDocumentRequest? inline)
    {
        if (inline == null) return;

        var doc = new VehicleDocument
        {
            DocId = Guid.NewGuid(),
            VehicleId = vehicle.VehicleId,
            DocumentType = documentType,
            DocumentNumber = NormalizeRequired(inline.DocumentNumber),
            Issuer = TrimOrNull(inline.Issuer),
            IssueDate = inline.IssueDate,
            ExpireDate = inline.ExpireDate,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now
        };
        _db.VehicleDocuments.Add(doc);
        vehicle.VehicleDocuments.Add(doc);
    }

    private async Task UpsertDriverLicenseFromImportAsync(Driver driver, IReadOnlyDictionary<string, string> row)
    {
        var licenseClass = NormalizeRequired(Get(row, "LicenseClass", "Hang bang")).ToUpperInvariant();
        var licenseNumber = NormalizeRequired(Get(row, "LicenseNumber", "So bang"));
        var license = driver.DriverLicenses.FirstOrDefault(l =>
            string.Equals(l.LicenseClass, licenseClass, StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.LicenseNumber, licenseNumber, StringComparison.OrdinalIgnoreCase));

        license ??= _db.DriverLicenses.Local.FirstOrDefault(l =>
            string.Equals(l.LicenseNumber, licenseNumber, StringComparison.OrdinalIgnoreCase)
            || (l.DriverId == driver.DriverId && string.Equals(l.LicenseClass, licenseClass, StringComparison.OrdinalIgnoreCase)));

        license ??= await _db.DriverLicenses.FirstOrDefaultAsync(l =>
            l.LicenseNumber == licenseNumber
            || (l.DriverId == driver.DriverId && l.LicenseClass == licenseClass));

        if (license == null)
        {
            license = new DriverLicense
            {
                LicenseId = Guid.NewGuid(),
                DriverId = driver.DriverId,
                LicenseClass = licenseClass,
                CreatedAt = DateTime.Now
            };
            _db.DriverLicenses.Add(license);
            driver.DriverLicenses.Add(license);
        }
        else if (license.DriverId.HasValue && license.DriverId.Value != driver.DriverId)
        {
            throw new InvalidOperationException($"License number '{licenseNumber}' already belongs to another driver");
        }
        else if (!driver.DriverLicenses.Any(l => l.LicenseId == license.LicenseId))
        {
            driver.DriverLicenses.Add(license);
        }

        license.DriverId = driver.DriverId;
        license.LicenseNumber = licenseNumber;
        license.IssueDate = GetDate(row, DateOnly.FromDateTime(DateTime.Today), "IssueDate", "Ngay cap");
        license.ExpiryDate = GetDate(row, DateOnly.FromDateTime(DateTime.Today), "ExpiryDate", "Ngay het han");
        license.Status = "ACTIVE";
    }

    private async Task RefreshVehicleStatusAsync(Vehicle vehicle)
    {
        if (vehicle.Status is "DELETED" or "MAINTENANCE") return;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var docs = vehicle.VehicleDocuments.Where(d => d.VehicleId == vehicle.VehicleId || d.VehicleId == null).ToList();
        var allValid = RequiredVehicleDocuments.All(required =>
            docs.Any(d => d.DocumentType == required
                          && d.Status == "ACTIVE"
                          && (d.ExpireDate == null || d.ExpireDate.Value > today)));

        vehicle.Status = allValid ? "ACTIVE" : "SUSPENDED_DOCS";
        await Task.CompletedTask;
    }

    private async Task RefreshDriverStatusAsync(Driver driver)
    {
        if (driver.Status == "DELETED") return;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var hasValidLicense = driver.DriverLicenses.Any(l => l.Status == "ACTIVE" && l.ExpiryDate > today);
        driver.Status = hasValidLicense ? "ACTIVE" : "SUSPENDED_DOCS";
        await Task.CompletedTask;
    }

    private async Task AddNotificationsAsync(IEnumerable<string> roleNames, Guid? specificUserId, string templateId, object parameters, string fallbackMessage, CancellationToken cancellationToken = default)
    {
        await EnsureFleetNotificationTemplatesAsync(cancellationToken);

        var normalizedRoles = roleNames.Select(r => r.ToUpperInvariant()).ToArray();
        var users = await _db.Users
            .Include(u => u.Role)
            .Where(u => u.Status != null && u.Status.ToUpper() == "ACTIVE" && 
                        (u.UserId == specificUserId || (u.Role != null && normalizedRoles.Contains(u.Role.RoleName.ToUpper()))))
            .ToListAsync(cancellationToken);
            
        Console.WriteLine($"[Notifications] Found {users.Count} users to send notification '{templateId}'");

        var payload = JsonSerializer.Serialize(parameters);
        foreach (var user in users)
        {
            _db.Notifications.Add(new Notification
            {
                NotiId = Guid.NewGuid(),
                UserId = user.UserId,
                TemplateId = templateId,
                Params = payload,
                IsRead = false,
                CreatedAt = DateTime.Now
            });
        }

        if (!users.Any())
            Console.WriteLine(fallbackMessage);
    }

    private async Task EnsureFleetNotificationTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var type = await _db.Messagetypes.FirstOrDefaultAsync(t => t.TypeName == "FLEET_ALERT", cancellationToken);
        if (type == null)
        {
            type = new Messagetype
            {
                TypeId = Guid.NewGuid(),
                TypeName = "FLEET_ALERT",
                Description = "Cảnh báo hồ sơ xe, tài xế và bảo dưỡng"
            };
            _db.Messagetypes.Add(type);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await EnsureTemplateAsync(type.TypeId, "NOTI_FLEET_DOC_EXPIRING", "Chứng từ xe sắp hết hạn", "Chứng từ {{DocumentType}} của xe {{TruckPlate}} sẽ hết hạn sau {{Days}} ngày.", cancellationToken);
        await EnsureTemplateAsync(type.TypeId, "NOTI_FLEET_DOC_EXPIRED", "Chứng từ xe đã hết hạn", "Chứng từ {{DocumentType}} của xe {{TruckPlate}} đã hết hạn.", cancellationToken);
        await EnsureTemplateAsync(type.TypeId, "NOTI_DRIVER_LICENSE_EXPIRING", "Bằng lái sắp hết hạn", "Bằng lái {{LicenseClass}} của tài xế {{FullName}} sẽ hết hạn sau {{Days}} ngày.", cancellationToken);
        await EnsureTemplateAsync(type.TypeId, "NOTI_DRIVER_LICENSE_EXPIRED", "Bằng lái đã hết hạn", "Bằng lái {{LicenseClass}} của tài xế {{FullName}} đã hết hạn.", cancellationToken);
        await EnsureTemplateAsync(type.TypeId, "NOTI_MAINTENANCE_ODOMETER", "Xe đến mốc bảo dưỡng", "Xe {{TruckPlate}} đã chạy {{CurrentOdometer}} KM, cần sắp xếp bảo dưỡng.", cancellationToken);
    }

    private async Task EnsureTemplateAsync(Guid typeId, string templateId, string title, string body, CancellationToken cancellationToken)
    {
        if (await _db.NotificationTemplates.AnyAsync(t => t.TemplateId == templateId, cancellationToken)) return;

        _db.NotificationTemplates.Add(new NotificationTemplate
        {
            TemplateId = templateId,
            TypeId = typeId,
            TitleTemplate = title,
            BodyTemplate = body,
            Channel = "IN_APP",
            Status = "ACTIVE"
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static VehicleFleetResponse ToVehicleResponse(Vehicle vehicle) => new()
    {
        VehicleId = vehicle.VehicleId,
        TruckPlate = vehicle.TruckPlate,
        Brand = vehicle.Brand,
        StandardFuelLiters = vehicle.StandardFuelLiters,
        VehicleType = vehicle.VehicleType,
        MaxWeight = vehicle.MaxWeight,
        MaxCbm = vehicle.MaxCbm,
        MinTemp = vehicle.MinTemp,
        MaxTemp = vehicle.MaxTemp,
        CurrentLocation = vehicle.CurrentLocation,
        CurrentOdometer = vehicle.CurrentOdometer,
        NextMaintenanceOdometer = vehicle.NextMaintenanceOdometer,
        NextMaintenanceDate = vehicle.NextMaintenanceDate,
        WarningDaysBeforeDue = vehicle.WarningDaysBeforeDue,
        WarningKmBeforeDue = vehicle.WarningKmBeforeDue,
        Status = vehicle.Status,
        Documents = vehicle.VehicleDocuments.Select(ToVehicleDocumentResponse).ToList()
    };

    private static DriverFleetResponse ToDriverResponse(Driver driver) => new()
    {
        DriverId = driver.DriverId,
        UserId = driver.UserId,
        FullName = driver.FullName,
        Email = driver.User?.Email,
        IdentityNumber = driver.IdentityNumber,
        PhoneNumber = driver.PhoneNumber,
        DateOfBirth = driver.DateOfBirth,
        JoinDate = driver.JoinDate,
        Status = driver.Status,
        Licenses = driver.DriverLicenses.Select(ToDriverLicenseResponse).ToList()
    };

    private static VehicleDocumentResponse ToVehicleDocumentResponse(VehicleDocument doc) => new()
    {
        DocId = doc.DocId,
        VehicleId = doc.VehicleId,
        DocumentType = doc.DocumentType,
        DocumentNumber = doc.DocumentNumber,
        Issuer = doc.Issuer,
        IssueDate = doc.IssueDate,
        ExpireDate = doc.ExpireDate,
        Status = doc.Status
    };

    private static DriverLicenseResponse ToDriverLicenseResponse(DriverLicense license) => new()
    {
        LicenseId = license.LicenseId,
        DriverId = license.DriverId,
        LicenseNumber = license.LicenseNumber,
        LicenseClass = license.LicenseClass,
        IssueDate = license.IssueDate,
        ExpiryDate = license.ExpiryDate,
        Status = license.Status
    };

    private static MaintenanceTicketResponse ToMaintenanceTicketResponse(MaintenanceTicket ticket) => new()
    {
        TicketId = ticket.TicketId,
        TicketCode = ticket.TicketCode,
        VehicleId = ticket.VehicleId,
        MaintenanceType = ticket.MaintenanceType,
        TriggeredAtOdometer = ticket.TriggeredAtOdometer,
        GarageName = ticket.GarageName,
        Description = ticket.Description,
        Cost = ticket.Cost,
        IssueDate = ticket.IssueDate,
        CompletionDate = ticket.CompletionDate,
        Status = ticket.Status,
        AttachmentUrl = ticket.AttachmentUrl
    };

    private static string NormalizeRequired(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Required value is missing");

        return normalized;
    }

    private static string NormalizeDocumentType(string? value) => NormalizeRequired(value).ToUpperInvariant();

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Get(IReadOnlyDictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
        {
            var key = row.Keys.FirstOrDefault(k => string.Equals(NormalizeHeader(k), NormalizeHeader(name), StringComparison.OrdinalIgnoreCase));
            if (key != null && !string.IsNullOrWhiteSpace(row[key])) return row[key];
        }

        return null;
    }

    private static decimal GetDecimal(IReadOnlyDictionary<string, string> row, decimal? fallback, params string[] names)
    {
        var value = Get(row, names);
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            || decimal.TryParse(value, NumberStyles.Any, CultureInfo.GetCultureInfo("vi-VN"), out parsed)
            ? parsed
            : fallback ?? 0;
    }

    private static double GetDouble(IReadOnlyDictionary<string, string> row, double fallback, params string[] names)
    {
        var value = Get(row, names);
        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Any, CultureInfo.GetCultureInfo("vi-VN"), out parsed)
            ? parsed
            : fallback;
    }

    private static DateOnly GetDate(IReadOnlyDictionary<string, string> row, DateOnly fallback, params string[] names)
        => GetNullableDate(row, names) ?? fallback;

    private static DateOnly? GetNullableDate(IReadOnlyDictionary<string, string> row, params string[] names)
    {
        var value = Get(row, names);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        if (DateOnly.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out date)) return date;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var oaDate))
            return DateOnly.FromDateTime(DateTime.FromOADate(oaDate));

        return null;
    }

    private static string NormalizeHeader(string value)
        => value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToUpperInvariant();

    public async Task<ApiResponse<IReadOnlyCollection<MaintenanceTicketResponse>>> GetMaintenanceTicketsAsync(Guid? vehicleId, string? status, int pageNumber = 1, int pageSize = 10)
    {
        var query = _db.MaintenanceTickets.AsNoTracking().AsQueryable();

        if (vehicleId.HasValue)
            query = query.Where(t => t.VehicleId == vehicleId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var responses = tickets.Select(ToMaintenanceTicketResponse).ToList();
        return ApiResponse<IReadOnlyCollection<MaintenanceTicketResponse>>.SuccessResponse(responses);
    }

    public async Task<ApiResponse<MaintenanceTicketResponse>> GetMaintenanceTicketByIdAsync(Guid ticketId)
    {
        var ticket = await _db.MaintenanceTickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);

        if (ticket == null)
            return ApiResponse<MaintenanceTicketResponse>.Failure("Maintenance ticket not found");

        return ApiResponse<MaintenanceTicketResponse>.SuccessResponse(ToMaintenanceTicketResponse(ticket));
    }

    public async Task<ApiResponse<MaintenanceTicketResponse>> UpdateMaintenanceTicketStatusAsync(Guid ticketId, string status)
    {
        var ticket = await _db.MaintenanceTickets
            .Include(t => t.Vehicle)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);

        if (ticket == null)
            return ApiResponse<MaintenanceTicketResponse>.Failure("Maintenance ticket not found");

        var normalizedStatus = status.ToUpperInvariant();
        if (normalizedStatus != "OPEN" && normalizedStatus != "IN_PROGRESS" && normalizedStatus != "CANCELLED" && normalizedStatus != "RESOLVED")
            return ApiResponse<MaintenanceTicketResponse>.Failure("Invalid status");

        ticket.Status = normalizedStatus;

        if (ticket.Vehicle != null)
        {
            if (normalizedStatus == "IN_PROGRESS" || normalizedStatus == "OPEN")
            {
                ticket.Vehicle.Status = "MAINTENANCE";
            }
            else if (normalizedStatus == "CANCELLED")
            {
                var hasOtherActive = await _db.MaintenanceTickets.AnyAsync(t =>
                    t.VehicleId == ticket.VehicleId &&
                    t.TicketId != ticket.TicketId &&
                    (t.Status == "OPEN" || t.Status == "IN_PROGRESS"));

                if (!hasOtherActive)
                {
                    ticket.Vehicle.Status = "ACTIVE";
                }
            }
        }

        await _db.SaveChangesAsync();
        return ApiResponse<MaintenanceTicketResponse>.SuccessResponse(ToMaintenanceTicketResponse(ticket), $"Status updated to {normalizedStatus}");
    }

    public async Task<ApiResponse<string>> UploadMaintenanceTicketDocumentAsync(Guid ticketId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return ApiResponse<string>.Failure("No file uploaded or file is empty");

        var ticket = await _db.MaintenanceTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
        if (ticket == null)
            return ApiResponse<string>.Failure("Maintenance ticket not found");

        var uploadResult = await _fileService.UploadFileAsync(file);
        if (uploadResult == null)
            return ApiResponse<string>.Failure("Failed to upload document");

        ticket.AttachmentUrl = uploadResult;
        await _db.SaveChangesAsync();

        return ApiResponse<string>.SuccessResponse(uploadResult, "Document uploaded successfully");
    }

    public async Task<ApiResponse<IReadOnlyCollection<MaintenanceTicketResponse>>> GetVehicleMaintenanceHistoryAsync(Guid vehicleId)
    {
        var tickets = await _db.MaintenanceTickets
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && t.Status == "RESOLVED")
            .OrderByDescending(t => t.CompletionDate)
            .ToListAsync();

        var responses = tickets.Select(ToMaintenanceTicketResponse).ToList();
        return ApiResponse<IReadOnlyCollection<MaintenanceTicketResponse>>.SuccessResponse(responses);
    }

    public async Task<ApiResponse<VehicleFleetResponse>> MarkVehicleUnavailableAsync(Guid vehicleId, string reason)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == vehicleId && v.Status != "DELETED");
        if (vehicle == null)
            return ApiResponse<VehicleFleetResponse>.Failure("Vehicle not found");

        vehicle.Status = "INACTIVE";
        await _db.SaveChangesAsync();

        return ApiResponse<VehicleFleetResponse>.SuccessResponse(ToVehicleResponse(vehicle), "Vehicle marked unavailable successfully");
    }

    public async Task<ApiResponse<MaintenanceForecastResponse>> GetVehicleMaintenanceForecastAsync(Guid vehicleId, Guid? tripId)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == vehicleId && v.Status != "DELETED");
        if (vehicle == null)
            return ApiResponse<MaintenanceForecastResponse>.Failure("Vehicle not found");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var isDueByDate = vehicle.NextMaintenanceDate.HasValue && today >= vehicle.NextMaintenanceDate.Value;
        var isDueByKm = vehicle.CurrentOdometer >= vehicle.NextMaintenanceOdometer;

        var isWarningByDate = vehicle.NextMaintenanceDate.HasValue && !isDueByDate &&
            (vehicle.NextMaintenanceDate.Value.DayNumber - today.DayNumber <= vehicle.WarningDaysBeforeDue);
        var isWarningByKm = !isDueByKm &&
            (vehicle.NextMaintenanceOdometer - vehicle.CurrentOdometer <= vehicle.WarningKmBeforeDue);

        var isOverrunForecast = false;
        double? tripDistance = null;

        if (tripId.HasValue)
        {
            var trip = await _db.MasterTrips.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == tripId.Value);
            if (trip != null && trip.TotalDistanceKm.HasValue)
            {
                tripDistance = (double)trip.TotalDistanceKm.Value;
                if (vehicle.CurrentOdometer + tripDistance.Value > vehicle.NextMaintenanceOdometer)
                {
                    isOverrunForecast = true;
                }
            }
        }

        var headroomKm = vehicle.NextMaintenanceOdometer - vehicle.CurrentOdometer;
        var remainingDays = vehicle.NextMaintenanceDate.HasValue
            ? vehicle.NextMaintenanceDate.Value.DayNumber - today.DayNumber
            : 365;

        var forecastStatus = "SAFE";
        var message = "Vehicle is safe for dispatch";

        if (isDueByDate || isDueByKm || isOverrunForecast)
        {
            forecastStatus = "OVERDUE";
            message = isOverrunForecast
                ? $"Trip distance of {tripDistance:F1} km exceeds available odometer headroom of {headroomKm:F1} km"
                : (isDueByKm ? "Vehicle has exceeded maintenance mileage limit" : "Vehicle is overdue for scheduled maintenance date");
        }
        else if (isWarningByDate || isWarningByKm)
        {
            forecastStatus = "WARNING";
            message = isWarningByKm
                ? $"Vehicle is approaching mileage limit. Headroom: {headroomKm:F1} km"
                : $"Vehicle is approaching due date. Remaining days: {remainingDays}";
        }

        var forecast = new MaintenanceForecastResponse
        {
            VehicleId = vehicle.VehicleId,
            TruckPlate = vehicle.TruckPlate,
            IsDueByDate = isDueByDate,
            IsDueByKm = isDueByKm,
            IsWarningByDate = isWarningByDate,
            IsWarningByKm = isWarningByKm,
            IsOverrunForecast = isOverrunForecast,
            HeadroomKm = headroomKm,
            RemainingDays = remainingDays,
            ForecastStatus = forecastStatus,
            Message = message
        };

        return ApiResponse<MaintenanceForecastResponse>.SuccessResponse(forecast);
    }

    private async Task<double> GetSystemConfigDoubleAsync(string key, double fallback)
    {
        var value = await _db.SystemConfigs
            .AsNoTracking()
            .Where(c => c.Key == key)
            .Select(c => c.Value)
            .FirstOrDefaultAsync();

        return double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private async Task<int> GetSystemConfigIntAsync(string key, int fallback)
    {
        var value = await _db.SystemConfigs
            .AsNoTracking()
            .Where(c => c.Key == key)
            .Select(c => c.Value)
            .FirstOrDefaultAsync();

        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static class SpreadsheetReader
    {
        public static async Task<IReadOnlyCollection<Dictionary<string, string>>> ReadAsync(IFormFile file)
        {
            if (file.Length == 0) throw new InvalidOperationException("Excel file is empty");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            await using var stream = file.OpenReadStream();
            return extension == ".csv"
                ? await ReadCsvAsync(stream)
                : ReadXlsx(stream);
        }

        private static async Task<IReadOnlyCollection<Dictionary<string, string>>> ReadCsvAsync(Stream stream)
        {
            using var reader = new StreamReader(stream);
            var headerLine = await reader.ReadLineAsync();
            if (headerLine == null) return Array.Empty<Dictionary<string, string>>();

            var headers = headerLine.Split(',').Select(h => h.Trim()).ToArray();
            var rows = new List<Dictionary<string, string>>();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = line.Split(',');
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Length; i++)
                    row[headers[i]] = i < values.Length ? values[i].Trim() : string.Empty;
                rows.Add(row);
            }

            return rows;
        }

        private static IReadOnlyCollection<Dictionary<string, string>> ReadXlsx(Stream stream)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var sharedStrings = ReadSharedStrings(archive);
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
                ?? throw new InvalidOperationException("Excel sheet1.xml not found");

            using var sheetStream = sheetEntry.Open();
            var document = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var rows = document.Descendants(ns + "row").ToList();
            if (!rows.Any()) return Array.Empty<Dictionary<string, string>>();

            var headerCells = ReadCells(rows[0], ns, sharedStrings);
            var headers = headerCells.OrderBy(c => c.Key).Select(c => c.Value).ToArray();
            var result = new List<Dictionary<string, string>>();

            foreach (var rowElement in rows.Skip(1))
            {
                var values = ReadCells(rowElement, ns, sharedStrings);
                if (!values.Any()) continue;

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Length; i++)
                    row[headers[i]] = values.TryGetValue(i + 1, out var value) ? value : string.Empty;

                if (row.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                    result.Add(row);
            }

            return result;
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return new List<string>();

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            return document.Descendants(ns + "si")
                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
                .ToList();
        }

        private static Dictionary<int, string> ReadCells(XElement row, XNamespace ns, IReadOnlyList<string> sharedStrings)
        {
            var cells = new Dictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? string.Empty;
                var columnIndex = GetColumnIndex(reference);
                var type = cell.Attribute("t")?.Value;
                var rawValue = cell.Element(ns + "v")?.Value ?? string.Empty;

                if (type == "s" && int.TryParse(rawValue, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                    cells[columnIndex] = sharedStrings[sharedIndex];
                else if (type == "inlineStr")
                    cells[columnIndex] = string.Concat(cell.Descendants(ns + "t").Select(t => t.Value));
                else
                    cells[columnIndex] = rawValue;
            }

            return cells;
        }

        private static int GetColumnIndex(string cellReference)
        {
            var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray());
            var index = 0;
            foreach (var letter in letters)
                index = index * 26 + (char.ToUpperInvariant(letter) - 'A' + 1);

            return index;
        }
    }
}
