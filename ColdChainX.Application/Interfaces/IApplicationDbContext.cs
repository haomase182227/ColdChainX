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
        DbSet<WarehouseZone> WarehouseZones { get; }
        DbSet<WarehouseLocation> WarehouseLocations { get; }
        DbSet<WarehouseReceipt> WarehouseReceipts { get; }
        DbSet<InboundAsn> InboundAsns { get; }

        DbSet<OutboundOrder> OutboundOrders { get; }
        DbSet<OutboundOrderItem> OutboundOrderItems { get; }

        DbSet<TransportOrder> TransportOrders { get; }
        DbSet<Customer> Customers { get; }
        DbSet<MasterTrip> MasterTrips { get; }
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
        DbSet<Notification> Notifications { get; }
        DbSet<NotificationTemplate> NotificationTemplates { get; }
        DbSet<Messagetype> Messagetypes { get; }
        DbSet<TransportDocument> TransportDocuments { get; }

        DatabaseFacade Database { get; }
        Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
