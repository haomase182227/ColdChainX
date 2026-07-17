using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.Interfaces;

public interface IFleetManagementService
{
    Task<ApiResponse<IReadOnlyCollection<VehicleFleetResponse>>> GetVehiclesAsync();
    Task<ApiResponse<VehicleFleetResponse>> GetVehicleByIdAsync(Guid vehicleId);
    Task<ApiResponse<VehicleFleetResponse>> CreateVehicleAsync(CreateVehicleRequest request);
    Task<ApiResponse<VehicleFleetResponse>> UpdateVehicleAsync(Guid vehicleId, ColdChainX.Application.DTOs.VehicleUpdateRequest request);
    Task<ApiResponse<bool>> SoftDeleteVehicleAsync(Guid vehicleId);
    Task<ApiResponse<ImportResultResponse>> ImportVehiclesAsync(IFormFile excelFile);

    Task<ApiResponse<IReadOnlyCollection<DriverFleetResponse>>> GetDriversAsync();
    Task<ApiResponse<DriverFleetResponse>> GetDriverByIdAsync(Guid driverId);
    Task<ApiResponse<DriverFleetResponse>> CreateDriverAsync(CreateDriverRequest request);
    Task<ApiResponse<DriverFleetResponse>> UpdateDriverAsync(Guid driverId, UpdateDriverRequest request);
    Task<ApiResponse<bool>> SoftDeleteDriverAsync(Guid driverId);
    Task<ApiResponse<ImportResultResponse>> ImportDriversAsync(IFormFile excelFile);
    Task<ApiResponse<PagedList<DriverTripHistoryResponseDto>>> GetDriverTripHistoryAsync(Guid driverId, int pageNumber = 1, int pageSize = 10);

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

    Task<ApiResponse<VehicleFleetResponse>> SyncOdometerAsync(SyncOdometerRequest request, Guid? updatedBy = null, string? photoUrl = null);
    Task<ApiResponse<MaintenanceTicketResponse>> CreateMaintenanceTicketAsync(Guid vehicleId, CreateMaintenanceTicketRequest request, Guid createdBy);
    Task<ApiResponse<MaintenanceTicketResponse>> CompleteMaintenanceTicketAsync(Guid ticketId, CompleteMaintenanceTicketRequest request);
    Task<ApiResponse<PagedList<MaintenanceTicketResponse>>> GetMaintenanceTicketsAsync(Guid? vehicleId, string? status, int pageNumber = 1, int pageSize = 10);
    Task<ApiResponse<MaintenanceTicketResponse>> GetMaintenanceTicketByIdAsync(Guid ticketId);
    Task<ApiResponse<MaintenanceTicketResponse>> UpdateMaintenanceTicketStatusAsync(Guid ticketId, string status);
    Task<ApiResponse<string>> UploadMaintenanceTicketDocumentAsync(Guid ticketId, IFormFile file);
    Task<ApiResponse<IReadOnlyCollection<MaintenanceTicketResponse>>> GetVehicleMaintenanceHistoryAsync(Guid vehicleId);
    Task<ApiResponse<VehicleFleetResponse>> MarkVehicleUnavailableAsync(Guid vehicleId, string reason);
    Task<ApiResponse<MaintenanceForecastResponse>> GetVehicleMaintenanceForecastAsync(Guid vehicleId, Guid? tripId);
    Task RunComplianceScanAsync(CancellationToken cancellationToken = default);
}
