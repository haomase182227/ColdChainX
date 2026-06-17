using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.Interfaces;

public interface IFleetManagementService
{
    Task<ApiResponse<IReadOnlyCollection<VehicleFleetResponse>>> GetVehiclesAsync();
    Task<ApiResponse<VehicleFleetResponse>> GetVehicleByIdAsync(Guid vehicleId);
    Task<ApiResponse<VehicleFleetResponse>> CreateVehicleAsync(CreateVehicleRequest request);
    Task<ApiResponse<bool>> SoftDeleteVehicleAsync(Guid vehicleId);
    Task<ApiResponse<ImportResultResponse>> ImportVehiclesAsync(IFormFile excelFile);

    Task<ApiResponse<IReadOnlyCollection<DriverFleetResponse>>> GetDriversAsync();
    Task<ApiResponse<DriverFleetResponse>> GetDriverByIdAsync(Guid driverId);
    Task<ApiResponse<DriverFleetResponse>> CreateDriverAsync(CreateDriverRequest request);
    Task<ApiResponse<bool>> SoftDeleteDriverAsync(Guid driverId);
    Task<ApiResponse<ImportResultResponse>> ImportDriversAsync(IFormFile excelFile);

    Task<ApiResponse<IReadOnlyCollection<VehicleDocumentResponse>>> GetVehicleDocumentsAsync(Guid? vehicleId);
    Task<ApiResponse<VehicleDocumentResponse>> GetVehicleDocumentByIdAsync(Guid docId);
    Task<ApiResponse<VehicleDocumentResponse>> CreateVehicleDocumentAsync(Guid vehicleId, CreateVehicleDocumentRequest request);
    Task<ApiResponse<VehicleDocumentResponse>> UpdateVehicleDocumentAsync(Guid docId, UpdateVehicleDocumentRequest request);
    Task<ApiResponse<bool>> DeleteVehicleDocumentAsync(Guid docId);
    Task<ApiResponse<ImportResultResponse>> ImportVehicleDocumentsAsync(IFormFile excelFile);

    Task<ApiResponse<IReadOnlyCollection<DriverLicenseResponse>>> GetDriverLicensesAsync(Guid? driverId);
    Task<ApiResponse<DriverLicenseResponse>> GetDriverLicenseByIdAsync(Guid licenseId);
    Task<ApiResponse<DriverLicenseResponse>> CreateDriverLicenseAsync(Guid driverId, CreateDriverLicenseRequest request);
    Task<ApiResponse<DriverLicenseResponse>> UpdateDriverLicenseAsync(Guid licenseId, UpdateDriverLicenseRequest request);
    Task<ApiResponse<bool>> DeleteDriverLicenseAsync(Guid licenseId);
    Task<ApiResponse<ImportResultResponse>> ImportDriverLicensesAsync(IFormFile excelFile);

    Task<ApiResponse<VehicleFleetResponse>> SyncOdometerAsync(string truckPlate, SyncOdometerRequest request);
    Task<ApiResponse<MaintenanceTicketResponse>> CreateMaintenanceTicketAsync(Guid vehicleId, CreateMaintenanceTicketRequest request, Guid createdBy);
    Task<ApiResponse<MaintenanceTicketResponse>> CompleteMaintenanceTicketAsync(Guid ticketId, CompleteMaintenanceTicketRequest request);
    Task RunComplianceScanAsync(CancellationToken cancellationToken = default);
}
