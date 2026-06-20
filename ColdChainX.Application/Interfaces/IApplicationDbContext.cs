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
        DbSet<WarehouseReceiptItem> WarehouseReceiptItems { get; }
        DbSet<InventoryStock> InventoryStocks { get; }
        DbSet<InventoryAdjustment> InventoryAdjustments { get; }
        DbSet<InventoryHold> InventoryHolds { get; }
        DbSet<InventoryMovement> InventoryMovements { get; }
        DbSet<InventoryBatch> InventoryBatches { get; }
        DbSet<OutboundOrder> OutboundOrders { get; }
        DbSet<OutboundOrderItem> OutboundOrderItems { get; }
        DbSet<InventoryAllocation> InventoryAllocations { get; }
        DbSet<WarehouseEvidenceAttachment> WarehouseEvidenceAttachments { get; }
        DbSet<CycleCountPlan> CycleCountPlans { get; }
        DbSet<CycleCountEntry> CycleCountEntries { get; }
        DbSet<TransportOrder> TransportOrders { get; }
        DbSet<Customer> Customers { get; }
        DbSet<MasterTrip> MasterTrips { get; }
        DbSet<Invoice> Invoices { get; }
        DbSet<InvoiceLine> InvoiceLines { get; }
        DbSet<Quotation> Quotations { get; }
        DbSet<ComplianceZoningRule> ComplianceZoningRules { get; }
        DbSet<AttachmentAuditHistory> AttachmentAuditHistories { get; }
        DbSet<PricingMatrix> PricingMatrices { get; }
        DbSet<IncidentReport> IncidentReports { get; }
        DbSet<Claim> Claims { get; }
        DbSet<ClaimEvidence> ClaimEvidences { get; }

        DatabaseFacade Database { get; }
        Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
