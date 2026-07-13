using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ColdChainX.Core.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Interfaces
{
    public interface IApplicationDbContext
    {
        DbSet<User> Users { get; }
        DbSet<Role> Roles { get; }
        DbSet<Warehouse> Warehouses { get; }
        DbSet<WarehouseReceipt> WarehouseReceipts { get; }
        DbSet<WeightTier> WeightTiers { get; }
        DbSet<InboundAsn> InboundAsns { get; }

        DbSet<OutboundOrder> OutboundOrders { get; }
        DbSet<OutboundOrderItem> OutboundOrderItems { get; }

        DbSet<TransportOrder> TransportOrders { get; }
        DbSet<Customer> Customers { get; }
        DbSet<Driver> Drivers { get; }
        DbSet<MasterTrip> MasterTrips { get; }
        DbSet<TripDriver> TripDrivers { get; }
        DbSet<DriverWorkLog> DriverWorkLogs { get; }
        DbSet<Invoice> Invoices { get; }
        DbSet<InvoiceLine> InvoiceLines { get; }
        DbSet<Quotation> Quotations { get; }
        DbSet<ComplianceZoningRule> ComplianceZoningRules { get; }
        DbSet<PricingMatrix> PricingMatrices { get; }
        DbSet<IncidentReport> IncidentReports { get; }
        DbSet<Claim> Claims { get; }
        DbSet<ClaimEvidence> ClaimEvidences { get; }
        DbSet<Lpn> Lpns { get; }
        DbSet<PenaltyBill> PenaltyBills { get; }
        DbSet<DeliveryEpod> DeliveryEpods { get; }
        DbSet<ContractAppendix> ContractAppendices { get; }
        DbSet<InboundReturnSlip> InboundReturnSlips { get; }
        DbSet<LpnDeliveryConfirmation> LpnDeliveryConfirmations { get; }
        DbSet<TelemetryLog> TelemetryLogs { get; }
        DbSet<Seal> Seals { get; }
        DbSet<Notification> Notifications { get; }
        DbSet<NotificationTemplate> NotificationTemplates { get; }
        DbSet<Messagetype> Messagetypes { get; }
        DbSet<TransportDocument> TransportDocuments { get; }
        DbSet<TripStop> TripStops { get; }
        DbSet<Location> Locations { get; }
        DbSet<Vehicle> Vehicles { get; }
        DbSet<ReturnedItem> ReturnedItems { get; }
        DbSet<MaintenanceTicket> MaintenanceTickets { get; }
        DbSet<VehicleOdometerLog> VehicleOdometerLogs { get; }
        DbSet<TripStopEvent> TripStopEvents { get; }
        DbSet<DetentionCharge> DetentionCharges { get; }
        DbSet<IncidentEvidence> IncidentEvidences { get; }

        DatabaseFacade Database { get; }
        Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
