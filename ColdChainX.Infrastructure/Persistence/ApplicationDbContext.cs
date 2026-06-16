using System;
using System.Collections.Generic;
using ColdChainX.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Persistence;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AlertLog> AlertLogs { get; set; }

    public virtual DbSet<Claim> Claims { get; set; }

    public virtual DbSet<ClaimEvidence> ClaimEvidences { get; set; }

    public virtual DbSet<ChatMessage> ChatMessages { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerContract> CustomerContracts { get; set; }

    public virtual DbSet<DeliveryEpod> DeliveryEpods { get; set; }

    public virtual DbSet<Driver> Drivers { get; set; }

    public virtual DbSet<DriverLicense> DriverLicenses { get; set; }

    public virtual DbSet<ExpenseAdvance> ExpenseAdvances { get; set; }

    public virtual DbSet<ExpenseReceipt> ExpenseReceipts { get; set; }

    public virtual DbSet<GeoFence> GeoFences { get; set; }

    public virtual DbSet<IncidentReport> IncidentReports { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceLine> InvoiceLines { get; set; }

    public virtual DbSet<IotDevice> IotDevices { get; set; }

    public virtual DbSet<InboundAsn> InboundAsns { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<MaintenanceTicket> MaintenanceTickets { get; set; }

    public virtual DbSet<MasterTrip> MasterTrips { get; set; }

    public virtual DbSet<Messagetype> Messagetypes { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NotificationTemplate> NotificationTemplates { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<PricingMatrix> PricingMatrices { get; set; }

    public virtual DbSet<Quotation> Quotations { get; set; }

    public virtual DbSet<ReturnedItem> ReturnedItems { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<RouteMaster> RouteMasters { get; set; }

    public virtual DbSet<Seal> Seals { get; set; }

    public virtual DbSet<SystemConfig> SystemConfigs { get; set; }

    public virtual DbSet<TelemetryLog> TelemetryLogs { get; set; }

    public virtual DbSet<TransportDocument> TransportDocuments { get; set; }

    public virtual DbSet<TransportOrder> TransportOrders { get; set; }

    public virtual DbSet<TripStop> TripStops { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<Vehicle> Vehicles { get; set; }

    public virtual DbSet<VehicleDocument> VehicleDocuments { get; set; }

    public virtual DbSet<Warehouse> Warehouses { get; set; }

    public virtual DbSet<WarehouseReceipt> WarehouseReceipts { get; set; }

    public virtual DbSet<WarehouseReceiptItem> WarehouseReceiptItems { get; set; }

    public virtual DbSet<WeightTier> WeightTiers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Set default schema to public for PostgreSQL
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<AlertLog>(entity =>
        {
            entity.HasKey(e => e.AlertId).HasName("alert_logs_pkey");

            entity.ToTable("alert_logs");

            entity.Property(e => e.AlertId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("alert_id");
            entity.Property(e => e.AlertType)
                .HasMaxLength(50)
                .HasColumnName("alert_type");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Latitude)
                .HasPrecision(10, 7)
                .HasColumnName("latitude");
            entity.Property(e => e.Longitude)
                .HasPrecision(10, 7)
                .HasColumnName("longitude");
            entity.Property(e => e.ResolutionNote).HasColumnName("resolution_note");
            entity.Property(e => e.ResolvedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.ResolvedBy).HasColumnName("resolved_by");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'NEW'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TripId).HasColumnName("trip_id");
            entity.Property(e => e.Value)
                .HasPrecision(5, 2)
                .HasColumnName("value");

            entity.HasOne(d => d.ResolvedByNavigation).WithMany(p => p.AlertLogs)
                .HasForeignKey(d => d.ResolvedBy)
                .HasConstraintName("fk_al_users");

            entity.HasOne(d => d.Trip).WithMany(p => p.AlertLogs)
                .HasForeignKey(d => d.TripId)
                .HasConstraintName("fk_al_mtrip");
        });

        modelBuilder.Entity<Claim>(entity =>
        {
            entity.HasKey(e => e.ClaimId).HasName("claims_pkey");

            entity.ToTable("claims");

            entity.HasIndex(e => e.ClaimCode, "claims_claim_code_key").IsUnique();

            entity.Property(e => e.ClaimId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("claim_id");
            entity.Property(e => e.ClaimCode)
                .HasMaxLength(50)
                .HasColumnName("claim_code");
            entity.Property(e => e.ClaimType)
                .HasMaxLength(50)
                .HasColumnName("claim_type");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.FaultOwner)
                .HasMaxLength(50)
                .HasColumnName("fault_owner");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.ResolutionNote).HasColumnName("resolution_note");
            entity.Property(e => e.ResolvedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'OPEN'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Order).WithMany(p => p.Claims)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("fk_claims_to");
        });

        modelBuilder.Entity<ClaimEvidence>(entity =>
        {
            entity.HasKey(e => e.EvidenceId).HasName("claim_evidences_pkey");

            entity.ToTable("claim_evidences");

            entity.Property(e => e.EvidenceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("evidence_id");
            entity.Property(e => e.AlertId).HasColumnName("alert_id");
            entity.Property(e => e.ClaimId).HasColumnName("claim_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DocId).HasColumnName("doc_id");
            entity.Property(e => e.EvidenceType)
                .HasMaxLength(30)
                .HasColumnName("evidence_type");
            entity.Property(e => e.ImageUrl)
                .HasMaxLength(255)
                .HasColumnName("image_url");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");

            entity.HasOne(d => d.Alert).WithMany(p => p.ClaimEvidences)
                .HasForeignKey(d => d.AlertId)
                .HasConstraintName("fk_ce_alert");

            entity.HasOne(d => d.Claim).WithMany(p => p.ClaimEvidences)
                .HasForeignKey(d => d.ClaimId)
                .HasConstraintName("fk_ce_claims");

            entity.HasOne(d => d.Doc).WithMany(p => p.ClaimEvidences)
                .HasForeignKey(d => d.DocId)
                .HasConstraintName("fk_ce_doc");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.ClaimEvidences)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_ce_users");
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("chat_messages_pkey");

            entity.ToTable("chat_messages");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");
            entity.Property(e => e.ReceiverId).HasColumnName("receiver_id");
            entity.Property(e => e.MessageContent).HasColumnName("message_content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");

            entity.HasOne(d => d.Order).WithMany(p => p.ChatMessages)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_chat_order");

            entity.HasOne(d => d.Sender).WithMany()
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_chat_sender");

            entity.HasOne(d => d.Receiver).WithMany()
                .HasForeignKey(d => d.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_chat_receiver");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("customers_pkey");

            entity.ToTable("customers");

            entity.HasIndex(e => e.TaxCode, "customers_tax_code_key").IsUnique();

            entity.Property(e => e.CustomerId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("customer_id");
            entity.Property(e => e.Address)
                .HasMaxLength(200)
                .HasColumnName("address");
            entity.Property(e => e.CompanyName)
                .HasMaxLength(150)
                .HasColumnName("company_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(200)
                .HasColumnName("email");
            entity.Property(e => e.PaymentTerm)
                .HasDefaultValue(30)
                .HasColumnName("payment_term");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TaxCode)
                .HasMaxLength(20)
                .HasColumnName("tax_code");
        });

        modelBuilder.Entity<CustomerContract>(entity =>
        {
            entity.HasKey(e => e.ContractId).HasName("customer_contracts_pkey");

            entity.ToTable("customer_contracts");

            entity.HasIndex(e => e.ContractNumber, "customer_contracts_contract_number_key").IsUnique();

            entity.Property(e => e.ContractId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("contract_id");
            entity.Property(e => e.ContractNumber)
                .HasMaxLength(50)
                .HasColumnName("contract_number");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DraftHtmlContent).HasColumnName("draft_html_content");
            entity.Property(e => e.ExpiredDate).HasColumnName("expired_date");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(255)
                .HasColumnName("file_url");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.SignedDate).HasColumnName("signed_date");
            entity.Property(e => e.SignedFileUrl)
                .HasMaxLength(255)
                .HasColumnName("signed_file_url");
            entity.Property(e => e.SentAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("sent_at");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'DRAFT'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UploadedSignedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("uploaded_signed_at");
            entity.Property(e => e.VerifiedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("verified_at");
            entity.Property(e => e.VerifiedBy).HasColumnName("verified_by");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerContracts)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("fk_cc_customers");

            entity.HasOne(d => d.Order).WithMany(p => p.CustomerContracts)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("fk_cc_orders");
        });

        modelBuilder.Entity<DeliveryEpod>(entity =>
        {
            entity.HasKey(e => e.EpodId).HasName("delivery_epods_pkey");

            entity.ToTable("delivery_epods");

            entity.Property(e => e.EpodId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("epod_id");
            entity.Property(e => e.CheckinTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checkin_time");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliveryRating)
                .HasDefaultValue(5)
                .HasColumnName("delivery_rating");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.PdfUrl)
                .HasMaxLength(255)
                .HasColumnName("pdf_url");
            entity.Property(e => e.ReceiverName)
                .HasMaxLength(100)
                .HasColumnName("receiver_name");
            entity.Property(e => e.ReceiverPhone)
                .HasMaxLength(20)
                .HasColumnName("receiver_phone");
            entity.Property(e => e.SignImageUrl)
                .HasMaxLength(255)
                .HasColumnName("sign_image_url");
            entity.Property(e => e.SignLatitude)
                .HasPrecision(10, 7)
                .HasColumnName("sign_latitude");
            entity.Property(e => e.SignLongitude)
                .HasPrecision(10, 7)
                .HasColumnName("sign_longitude");
            entity.Property(e => e.SignedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("signed_at");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Order).WithMany(p => p.DeliveryEpods)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("fk_epod_to");
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(e => e.DriverId).HasName("drivers_pkey");

            entity.ToTable("drivers");

            entity.Property(e => e.DriverId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("driver_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'AVAILABLE'::character varying")
                .HasColumnName("status");
        });

        modelBuilder.Entity<DriverLicense>(entity =>
        {
            entity.HasKey(e => e.LicenseId).HasName("driver_licenses_pkey");

            entity.ToTable("driver_licenses");

            entity.HasIndex(e => e.LicenseNumber, "driver_licenses_license_number_key").IsUnique();

            entity.Property(e => e.LicenseId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("license_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentUrl)
                .HasMaxLength(255)
                .HasColumnName("document_url");
            entity.Property(e => e.DriverId).HasColumnName("driver_id");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.IssueDate).HasColumnName("issue_date");
            entity.Property(e => e.LicenseClass)
                .HasMaxLength(10)
                .HasColumnName("license_class");
            entity.Property(e => e.LicenseNumber)
                .HasMaxLength(20)
                .HasColumnName("license_number");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Driver).WithMany(p => p.DriverLicenses)
                .HasForeignKey(d => d.DriverId)
                .HasConstraintName("fk_dl_drivers");
        });

        modelBuilder.Entity<ExpenseAdvance>(entity =>
        {
            entity.HasKey(e => e.AdvanceId).HasName("expense_advances_pkey");

            entity.ToTable("expense_advances");

            entity.HasIndex(e => e.AdvanceCode, "expense_advances_advance_code_key").IsUnique();

            entity.Property(e => e.AdvanceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("advance_id");
            entity.Property(e => e.AdvanceCode)
                .HasMaxLength(50)
                .HasColumnName("advance_code");
            entity.Property(e => e.AdvancedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("advanced_date");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by");
            entity.Property(e => e.ClearanceStatus)
                .HasMaxLength(20)
                .HasDefaultValueSql("'OPEN'::character varying")
                .HasColumnName("clearance_status");
            entity.Property(e => e.ClearedAmount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("cleared_amount");
            entity.Property(e => e.DriverId).HasColumnName("driver_id");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(20)
                .HasDefaultValueSql("'CASH'::character varying")
                .HasColumnName("payment_method");
            entity.Property(e => e.ReturnedAmount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("returned_amount");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TripId).HasColumnName("trip_id");

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.ExpenseAdvances)
                .HasForeignKey(d => d.ApprovedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_ea_users");

            entity.HasOne(d => d.Driver).WithMany(p => p.ExpenseAdvances)
                .HasForeignKey(d => d.DriverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_ea_drivers");

            entity.HasOne(d => d.Trip).WithMany(p => p.ExpenseAdvances)
                .HasForeignKey(d => d.TripId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_ea_mtrip");
        });

        modelBuilder.Entity<ExpenseReceipt>(entity =>
        {
            entity.HasKey(e => e.ReceiptId).HasName("expense_receipts_pkey");

            entity.ToTable("expense_receipts");

            entity.Property(e => e.ReceiptId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("receipt_id");
            entity.Property(e => e.AdvanceId).HasColumnName("advance_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.ExpenseDate).HasColumnName("expense_date");
            entity.Property(e => e.ExpenseType)
                .HasMaxLength(30)
                .HasColumnName("expense_type");
            entity.Property(e => e.ImageUrl)
                .HasMaxLength(255)
                .HasColumnName("image_url");
            entity.Property(e => e.RejectReason)
                .HasMaxLength(255)
                .HasColumnName("reject_reason");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UploadedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("uploaded_at");
            entity.Property(e => e.VerifiedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("verified_at");
            entity.Property(e => e.VerifiedBy).HasColumnName("verified_by");

            entity.HasOne(d => d.Advance).WithMany(p => p.ExpenseReceipts)
                .HasForeignKey(d => d.AdvanceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_er_ea");

            entity.HasOne(d => d.VerifiedByNavigation).WithMany(p => p.ExpenseReceipts)
                .HasForeignKey(d => d.VerifiedBy)
                .HasConstraintName("fk_er_users");
        });

        modelBuilder.Entity<GeoFence>(entity =>
        {
            entity.HasKey(e => e.FenceId).HasName("geo_fences_pkey");

            entity.ToTable("geo_fences");

            entity.Property(e => e.FenceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("fence_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Location).WithMany(p => p.GeoFences)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("fk_geo_locations");
        });

        modelBuilder.Entity<IncidentReport>(entity =>
        {
            entity.HasKey(e => e.IncidentId).HasName("incident_reports_pkey");

            entity.ToTable("incident_reports");

            entity.Property(e => e.IncidentId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("incident_id");
            entity.Property(e => e.CurrentLatitude)
                .HasPrecision(10, 7)
                .HasColumnName("current_latitude");
            entity.Property(e => e.CurrentLongitude)
                .HasPrecision(10, 7)
                .HasColumnName("current_longitude");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IncidentType)
                .HasMaxLength(50)
                .HasColumnName("incident_type");
            entity.Property(e => e.ReportedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("reported_at");
            entity.Property(e => e.ReportedBy).HasColumnName("reported_by");
            entity.Property(e => e.ResolvedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");
            entity.Property(e => e.Severity)
                .HasMaxLength(20)
                .HasColumnName("severity");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'REPORTED'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TripId).HasColumnName("trip_id");

            entity.HasOne(d => d.ReportedByNavigation).WithMany(p => p.IncidentReports)
                .HasForeignKey(d => d.ReportedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_ir_users");

            entity.HasOne(d => d.Trip).WithMany(p => p.IncidentReports)
                .HasForeignKey(d => d.TripId)
                .HasConstraintName("fk_ir_mtrip");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.InvoiceId).HasName("invoices_pkey");

            entity.ToTable("invoices");

            entity.HasIndex(e => e.InvoiceCode, "invoices_invoice_code_key").IsUnique();

            entity.Property(e => e.InvoiceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("invoice_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DeductionAmount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("deduction_amount");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.GrandTotal)
                .HasPrecision(15, 2)
                .HasColumnName("grand_total");
            entity.Property(e => e.InvoiceCode)
                .HasMaxLength(50)
                .HasColumnName("invoice_code");
            entity.Property(e => e.IssuedDate).HasColumnName("issued_date");
            entity.Property(e => e.PaidAmount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("paid_amount");
            entity.Property(e => e.PdfUrl)
                .HasMaxLength(255)
                .HasColumnName("pdf_url");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'DRAFT'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.SubTotal)
                .HasPrecision(15, 2)
                .HasColumnName("sub_total");
            entity.Property(e => e.TaxAmount)
                .HasPrecision(15, 2)
                .HasColumnName("tax_amount");
            entity.Property(e => e.TaxRate)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("8.00")
                .HasColumnName("tax_rate");
            entity.Property(e => e.VatInvoiceNo)
                .HasMaxLength(50)
                .HasColumnName("vat_invoice_no");

            entity.HasOne(d => d.Customer).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_inv_customers");
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.HasKey(e => e.LineId).HasName("invoice_lines_pkey");

            entity.ToTable("invoice_lines");

            entity.Property(e => e.LineId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("line_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.ChargeType)
                .HasMaxLength(50)
                .HasColumnName("charge_type");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("1.00")
                .HasColumnName("quantity");
            entity.Property(e => e.TaxRate)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("8.00")
                .HasColumnName("tax_rate");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(15, 2)
                .HasColumnName("unit_price");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceLines)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_il_inv");

            entity.HasOne(d => d.Order).WithMany(p => p.InvoiceLines)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_il_to");
        });

        modelBuilder.Entity<IotDevice>(entity =>
        {
            entity.HasKey(e => e.DeviceId).HasName("iot_devices_pkey");

            entity.ToTable("iot_devices");

            entity.Property(e => e.DeviceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("device_id");
            entity.Property(e => e.BatteryLevel).HasColumnName("battery_level");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.LastPingTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_ping_time");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.IotDevices)
                .HasForeignKey(d => d.VehicleId)
                .HasConstraintName("fk_iot_vehicles");
        });

        modelBuilder.Entity<InboundAsn>(entity =>
        {
            entity.HasKey(e => e.AsnId).HasName("inbound_asn_pkey");

            entity.ToTable("inbound_asn");

            entity.HasIndex(e => e.AsnCode, "inbound_asn_asn_code_key").IsUnique();

            entity.Property(e => e.AsnId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("asn_id");
            entity.Property(e => e.AsnCode)
                .HasMaxLength(50)
                .HasColumnName("asn_code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.QrCodeValue)
                .HasMaxLength(500)
                .HasColumnName("qr_code_value");
            entity.Property(e => e.RequestedDropoffTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("requested_dropoff_time");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValueSql("'SCHEDULED'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Order).WithMany(p => p.InboundAsns)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_asn_order");
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.LocationId).HasName("locations_pkey");

            entity.ToTable("locations");

            entity.Property(e => e.LocationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("location_id");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Latitude)
                .HasPrecision(10, 7)
                .HasColumnName("latitude");
            entity.Property(e => e.Longitude)
                .HasPrecision(10, 7)
                .HasColumnName("longitude");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Customer).WithMany(p => p.Locations)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("fk_loc_customers");
        });

        modelBuilder.Entity<MaintenanceTicket>(entity =>
        {
            entity.HasKey(e => e.TicketId).HasName("maintenance_tickets_pkey");

            entity.ToTable("maintenance_tickets");

            entity.HasIndex(e => e.TicketCode, "maintenance_tickets_ticket_code_key").IsUnique();

            entity.Property(e => e.TicketId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("ticket_id");
            entity.Property(e => e.CompletionDate).HasColumnName("completion_date");
            entity.Property(e => e.Cost)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("cost");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.GarageName)
                .HasMaxLength(150)
                .HasColumnName("garage_name");
            entity.Property(e => e.IssueDate).HasColumnName("issue_date");
            entity.Property(e => e.MaintenanceType)
                .HasMaxLength(30)
                .HasColumnName("maintenance_type");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'OPEN'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TicketCode)
                .HasMaxLength(50)
                .HasColumnName("ticket_code");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.MaintenanceTickets)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mt_users");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.MaintenanceTickets)
                .HasForeignKey(d => d.VehicleId)
                .HasConstraintName("fk_mt_vehicles");
        });

        modelBuilder.Entity<MasterTrip>(entity =>
        {
            entity.HasKey(e => e.TripId).HasName("master_trips_pkey");

            entity.ToTable("master_trips");

            entity.Property(e => e.TripId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("trip_id");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DestinationLocationId).HasColumnName("destination_location_id");
            entity.Property(e => e.DriverId).HasColumnName("driver_id");
            entity.Property(e => e.OriginLocationId).HasColumnName("origin_location_id");
            entity.Property(e => e.PlannedEndTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_end_time");
            entity.Property(e => e.PlannedStartTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_start_time");
            entity.Property(e => e.StartedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PLANNED'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TargetTemperature)
                .HasPrecision(5, 2)
                .HasColumnName("target_temperature");
            entity.Property(e => e.TotalDistanceKm)
                .HasPrecision(8, 2)
                .HasColumnName("total_distance_km");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");

            entity.HasOne(d => d.DestinationLocation).WithMany(p => p.MasterTripDestinationLocations)
                .HasForeignKey(d => d.DestinationLocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mtrip_dest");

            entity.HasOne(d => d.Driver).WithMany(p => p.MasterTrips)
                .HasForeignKey(d => d.DriverId)
                .HasConstraintName("fk_mtrip_drivers");

            entity.HasOne(d => d.OriginLocation).WithMany(p => p.MasterTripOriginLocations)
                .HasForeignKey(d => d.OriginLocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mtrip_orig");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.MasterTrips)
                .HasForeignKey(d => d.VehicleId)
                .HasConstraintName("fk_mtrip_vehicles");
        });

        modelBuilder.Entity<Messagetype>(entity =>
        {
            entity.HasKey(e => e.TypeId).HasName("messagetype_pkey");

            entity.ToTable("messagetype");

            entity.Property(e => e.TypeId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("type_id");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.TypeName)
                .HasMaxLength(50)
                .HasColumnName("type_name");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotiId).HasName("notifications_pkey");

            entity.ToTable("notifications");

            entity.Property(e => e.NotiId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("noti_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Params)
                .HasColumnType("json")
                .HasColumnName("params");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");
            entity.Property(e => e.TemplateId)
                .HasMaxLength(50)
                .HasColumnName("template_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Order).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("fk_noti_order");

            entity.HasOne(d => d.Sender).WithMany(p => p.NotificationSenders)
                .HasForeignKey(d => d.SenderId)
                .HasConstraintName("fk_noti_sender");

            entity.HasOne(d => d.Template).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.TemplateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_noti_template");

            entity.HasOne(d => d.User).WithMany(p => p.NotificationUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_noti_users");
        });

        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId).HasName("notification_templates_pkey");

            entity.ToTable("notification_templates");

            entity.Property(e => e.TemplateId)
                .HasMaxLength(50)
                .HasColumnName("template_id");
            entity.Property(e => e.BodyTemplate).HasColumnName("body_template");
            entity.Property(e => e.Channel)
                .HasMaxLength(20)
                .HasColumnName("channel");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TitleTemplate)
                .HasMaxLength(100)
                .HasColumnName("title_template");
            entity.Property(e => e.TypeId).HasColumnName("type_id");

            entity.HasOne(d => d.Type).WithMany(p => p.NotificationTemplates)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_nt_msgtype");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.PermId).HasName("permissions_pkey");

            entity.ToTable("permissions");

            entity.HasIndex(e => e.PermCode, "permissions_perm_code_key").IsUnique();

            entity.Property(e => e.PermId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("perm_id");
            entity.Property(e => e.Module)
                .HasMaxLength(50)
                .HasColumnName("module");
            entity.Property(e => e.PermCode)
                .HasMaxLength(50)
                .HasColumnName("perm_code");
        });

        modelBuilder.Entity<PricingMatrix>(entity =>
        {
            entity.HasKey(e => e.PriceId).HasName("pricing_matrix_pkey");

            entity.ToTable("pricing_matrix");

            entity.Property(e => e.PriceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("price_id");
            entity.Property(e => e.DestCity)
                .HasMaxLength(50)
                .HasColumnName("dest_city");
            entity.Property(e => e.EffectiveDate).HasColumnName("effective_date");
            entity.Property(e => e.MaxValue)
                .HasPrecision(12, 4)
                .HasColumnName("max_value");
            entity.Property(e => e.MinCharge)
                .HasPrecision(15, 2)
                .HasColumnName("min_charge");
            entity.Property(e => e.MinValue)
                .HasPrecision(12, 4)
                .HasColumnName("min_value");
            entity.Property(e => e.OriginCity)
                .HasMaxLength(50)
                .HasColumnName("origin_city");
            entity.Property(e => e.PricingUnit)
                .HasMaxLength(20)
                .HasColumnName("pricing_unit");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(15, 2)
                .HasColumnName("unit_price");
        });

        modelBuilder.Entity<Quotation>(entity =>
        {
            entity.HasKey(e => e.QuoteId).HasName("quotations_pkey");

            entity.ToTable("quotations");

            entity.Property(e => e.QuoteId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("quote_id");
            entity.Property(e => e.BaseFreight)
                .HasPrecision(15, 2)
                .HasColumnName("base_freight");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FinalAmount)
                .HasPrecision(15, 2)
                .HasColumnName("final_amount");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(255)
                .HasColumnName("file_url");
            entity.Property(e => e.ChargeableWeightKg)
                .HasPrecision(10, 2)
                .HasColumnName("chargeable_weight_kg");
            entity.Property(e => e.DistanceKm)
                .HasPrecision(10, 2)
                .HasColumnName("distance_km");
            entity.Property(e => e.LastMileSurcharge)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("last_mile_surcharge");
            entity.Property(e => e.ManualAdjustment)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("manual_adjustment");
            entity.Property(e => e.AdditionalCharges)
                .HasColumnType("jsonb")
                .HasColumnName("additional_charges");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.OverrideReason)
                .HasMaxLength(500)
                .HasColumnName("override_reason");
            entity.Property(e => e.PricingSource)
                .HasMaxLength(30)
                .HasDefaultValueSql("'AUTO'::character varying")
                .HasColumnName("pricing_source");
            entity.Property(e => e.PricePerKg)
                .HasPrecision(15, 2)
                .HasColumnName("price_per_kg");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.SystemBaseFreight)
                .HasPrecision(15, 2)
                .HasColumnName("system_base_freight");
            entity.Property(e => e.VatPercentage)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("8")
                .HasColumnName("vat_percentage");
            entity.Property(e => e.VasAmount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("vas_amount");
            entity.Property(e => e.VatAmount)
                .HasPrecision(15, 2)
                .HasColumnName("vat_amount");
            entity.Property(e => e.VolumetricWeightKg)
                .HasPrecision(10, 2)
                .HasColumnName("volumetric_weight_kg");

            entity.HasOne(d => d.Order).WithMany(p => p.Quotations)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("fk_quote_to");
        });

        modelBuilder.Entity<ReturnedItem>(entity =>
        {
            entity.HasKey(e => e.ReturnId).HasName("returned_items_pkey");

            entity.ToTable("returned_items");

            entity.Property(e => e.ReturnId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("return_id");
            entity.Property(e => e.EpodId).HasColumnName("epod_id");
            entity.Property(e => e.ItemCode)
                .HasMaxLength(50)
                .HasColumnName("item_code");
            entity.Property(e => e.ItemName)
                .HasMaxLength(255)
                .HasColumnName("item_name");
            entity.Property(e => e.ProcessedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("processed_at");
            entity.Property(e => e.ProcessedBy).HasColumnName("processed_by");
            entity.Property(e => e.ProcessingStatus)
                .HasMaxLength(30)
                .HasDefaultValueSql("'PENDING_INSPECT'::character varying")
                .HasColumnName("processing_status");
            entity.Property(e => e.ReasonNote).HasColumnName("reason_note");
            entity.Property(e => e.ReasonType)
                .HasMaxLength(50)
                .HasColumnName("reason_type");
            entity.Property(e => e.ReturnedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("returned_at");
            entity.Property(e => e.ReturnedQty)
                .HasPrecision(10, 2)
                .HasColumnName("returned_qty");
            entity.Property(e => e.Unit)
                .HasMaxLength(20)
                .HasColumnName("unit");

            entity.HasOne(d => d.Epod).WithMany(p => p.ReturnedItems)
                .HasForeignKey(d => d.EpodId)
                .HasConstraintName("fk_ri_epod");

            entity.HasOne(d => d.ProcessedByNavigation).WithMany(p => p.ReturnedItems)
                .HasForeignKey(d => d.ProcessedBy)
                .HasConstraintName("fk_ri_users");
        });

        modelBuilder.Entity<RouteMaster>(entity =>
        {
            entity.HasKey(e => e.RouteId).HasName("route_master_pkey");

            entity.ToTable("route_master");

            entity.HasIndex(e => e.RouteCode, "route_master_route_code_key").IsUnique();

            entity.Property(e => e.RouteId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("route_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CutOffTime)
                .HasColumnType("time without time zone")
                .HasColumnName("cut_off_time");
            entity.Property(e => e.DestCity)
                .HasMaxLength(50)
                .HasColumnName("dest_city");
            entity.Property(e => e.OriginCity)
                .HasMaxLength(50)
                .HasColumnName("origin_city");
            entity.Property(e => e.RouteCode)
                .HasMaxLength(50)
                .HasColumnName("route_code");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TransitTime)
                .HasMaxLength(50)
                .HasColumnName("transit_time");
        });

        modelBuilder.Entity<SystemConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("system_configs_pkey");

            entity.ToTable("system_configs");

            entity.HasIndex(e => e.Key, "system_configs_key_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Key)
                .HasMaxLength(100)
                .HasColumnName("key");
            entity.Property(e => e.Value)
                .HasMaxLength(255)
                .HasColumnName("value");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
        });

        modelBuilder.Entity<WeightTier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("weight_tiers_pkey");

            entity.ToTable("weight_tiers");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.MinWeightKg)
                .HasPrecision(10, 2)
                .HasColumnName("min_weight_kg");
            entity.Property(e => e.MaxWeightKg)
                .HasPrecision(10, 2)
                .HasColumnName("max_weight_kg");
            entity.Property(e => e.PricePerKg)
                .HasPrecision(15, 2)
                .HasColumnName("price_per_kg");

            entity.HasOne(d => d.Route).WithMany(p => p.WeightTiers)
                .HasForeignKey(d => d.RouteId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_weight_tiers_route");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("roles_pkey");

            entity.ToTable("roles");

            entity.HasIndex(e => e.RoleName, "roles_role_name_key").IsUnique();

            entity.Property(e => e.RoleId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("role_id");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.RoleName)
                .HasMaxLength(50)
                .HasColumnName("role_name");

            entity.HasMany(d => d.Perms).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "RolePermission",
                    r => r.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("fk_rp_perms"),
                    l => l.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("fk_rp_roles"),
                    j =>
                    {
                        j.HasKey("RoleId", "PermId").HasName("role_permissions_pkey");
                        j.ToTable("role_permissions");
                        j.IndexerProperty<Guid>("RoleId").HasColumnName("role_id");
                        j.IndexerProperty<Guid>("PermId").HasColumnName("perm_id");
                    });
        });

        modelBuilder.Entity<Seal>(entity =>
        {
            entity.HasKey(e => e.SealId).HasName("seals_pkey");

            entity.ToTable("seals");

            entity.Property(e => e.SealId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("seal_id");
            entity.Property(e => e.AppliedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("applied_at");
            entity.Property(e => e.AppliedImageUrl)
                .HasMaxLength(255)
                .HasColumnName("applied_image_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.RemovedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("removed_at");
            entity.Property(e => e.RemovedImageUrl)
                .HasMaxLength(255)
                .HasColumnName("removed_image_url");
            entity.Property(e => e.SealCode)
                .HasMaxLength(50)
                .HasColumnName("seal_code");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'APPLIED'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.StopId).HasColumnName("stop_id");
            entity.Property(e => e.TripId).HasColumnName("trip_id");

            entity.HasOne(d => d.Stop).WithMany(p => p.Seals)
                .HasForeignKey(d => d.StopId)
                .HasConstraintName("fk_seals_ts");

            entity.HasOne(d => d.Trip).WithMany(p => p.Seals)
                .HasForeignKey(d => d.TripId)
                .HasConstraintName("fk_seals_mtrip");
        });

        modelBuilder.Entity<TelemetryLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("telemetry_logs_pkey");

            entity.ToTable("telemetry_logs");

            entity.Property(e => e.LogId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("log_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeviceId).HasColumnName("device_id");
            entity.Property(e => e.Latitude)
                .HasPrecision(10, 7)
                .HasColumnName("latitude");
            entity.Property(e => e.Longitude)
                .HasPrecision(10, 7)
                .HasColumnName("longitude");
            entity.Property(e => e.Temperature)
                .HasPrecision(5, 2)
                .HasColumnName("temperature");
            entity.Property(e => e.Timestamp)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("timestamp");
            entity.Property(e => e.TripId).HasColumnName("trip_id");

            entity.HasOne(d => d.Device).WithMany(p => p.TelemetryLogs)
                .HasForeignKey(d => d.DeviceId)
                .HasConstraintName("fk_tl_iot");

            entity.HasOne(d => d.Trip).WithMany(p => p.TelemetryLogs)
                .HasForeignKey(d => d.TripId)
                .HasConstraintName("fk_tl_mtrip");
        });

        modelBuilder.Entity<TransportDocument>(entity =>
        {
            entity.HasKey(e => e.DocId).HasName("transport_documents_pkey");

            entity.ToTable("transport_documents");

            entity.Property(e => e.DocId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("doc_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DocType)
                .HasMaxLength(50)
                .HasColumnName("doc_type");
            entity.Property(e => e.ImageUrl)
                .HasMaxLength(255)
                .HasColumnName("image_url");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.RejectReason)
                .HasMaxLength(255)
                .HasColumnName("reject_reason");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
            entity.Property(e => e.VerifiedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("verified_at");
            entity.Property(e => e.VerifiedBy).HasColumnName("verified_by");

            entity.HasOne(d => d.Order).WithMany(p => p.TransportDocuments)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("fk_td_to");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.TransportDocumentUploadedByNavigations)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_td_uploaded");

            entity.HasOne(d => d.VerifiedByNavigation).WithMany(p => p.TransportDocumentVerifiedByNavigations)
                .HasForeignKey(d => d.VerifiedBy)
                .HasConstraintName("fk_td_verified");
        });

        modelBuilder.Entity<TransportOrder>(entity =>
        {
            entity.HasKey(e => e.OrderId).HasName("transport_orders_pkey");

            entity.ToTable("transport_orders");

            entity.HasIndex(e => e.TrackingCode, "transport_orders_tracking_code_key").IsUnique();

            entity.Property(e => e.OrderId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("order_id");
            entity.Property(e => e.ActualCbm)
                .HasPrecision(8, 2)
                .HasColumnName("actual_cbm");
            entity.Property(e => e.ActualWeightKg)
                .HasPrecision(10, 2)
                .HasColumnName("actual_weight_kg");
            entity.Property(e => e.CargoValue)
                .HasPrecision(15, 2)
                .HasColumnName("cargo_value");
            entity.Property(e => e.Category)
                .HasMaxLength(50)
                .HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DestLocation).HasColumnName("dest_location");
            entity.Property(e => e.ExpectedCbm)
                .HasPrecision(8, 2)
                .HasColumnName("expected_cbm");
            entity.Property(e => e.ExpectedWeightKg)
                .HasPrecision(10, 2)
                .HasColumnName("expected_weight_kg");
            entity.Property(e => e.ItemName)
                .HasMaxLength(150)
                .HasColumnName("item_name");
            entity.Property(e => e.MasterTripId).HasColumnName("master_trip_id");
            entity.Property(e => e.PickupLocation).HasColumnName("pickup_location");
            entity.Property(e => e.PackingType)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Thùng'::character varying")
                .HasColumnName("packing_type");
            entity.Property(e => e.Quantity)
                .HasDefaultValue(1)
                .HasColumnName("quantity");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.TempCondition)
                .HasMaxLength(20)
                .HasColumnName("temp_condition");
            entity.Property(e => e.TrackingCode)
                .HasMaxLength(50)
                .HasColumnName("tracking_code");

            entity.HasOne(d => d.Customer).WithMany(p => p.TransportOrders)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("fk_to_customers");

            entity.HasOne(d => d.DestLocationNavigation).WithMany(p => p.TransportOrderDestLocationNavigations)
                .HasForeignKey(d => d.DestLocation)
                .HasConstraintName("fk_to_dest");

            entity.HasOne(d => d.MasterTrip).WithMany(p => p.TransportOrders)
                .HasForeignKey(d => d.MasterTripId)
                .HasConstraintName("fk_to_mtrip");

            entity.HasOne(d => d.PickupLocationNavigation).WithMany(p => p.TransportOrderPickupLocationNavigations)
                .HasForeignKey(d => d.PickupLocation)
                .HasConstraintName("fk_to_pickup");

            entity.HasOne(d => d.Route).WithMany(p => p.TransportOrders)
                .HasForeignKey(d => d.RouteId)
                .HasConstraintName("fk_to_route");
        });

        modelBuilder.Entity<TripStop>(entity =>
        {
            entity.HasKey(e => e.StopId).HasName("trip_stops_pkey");

            entity.ToTable("trip_stops");

            entity.Property(e => e.StopId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("stop_id");
            entity.Property(e => e.ActualArrivalTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("actual_arrival_time");
            entity.Property(e => e.ActualDepartureTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("actual_departure_time");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.PlannedArrivalTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_arrival_time");
            entity.Property(e => e.PlannedDepartureTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("planned_departure_time");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDING'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.StopSequence).HasColumnName("stop_sequence");
            entity.Property(e => e.StopType)
                .HasMaxLength(30)
                .HasColumnName("stop_type");
            entity.Property(e => e.TripId).HasColumnName("trip_id");

            entity.HasOne(d => d.Location).WithMany(p => p.TripStops)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("fk_ts_loc");

            entity.HasOne(d => d.Trip).WithMany(p => p.TripStops)
                .HasForeignKey(d => d.TripId)
                .HasConstraintName("fk_ts_mtrip");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Username, "users_username_key").IsUnique();

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .HasColumnName("full_name");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.RefreshToken)
                .HasMaxLength(255)
                .HasColumnName("refresh_token");
            entity.Property(e => e.RefreshTokenExpiryTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("refresh_token_expiry_time");
            entity.Property(e => e.RoleId)
                .HasColumnName("role_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("fk_users_roles");
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(e => e.VehicleId).HasName("vehicles_pkey");

            entity.ToTable("vehicles");

            entity.HasIndex(e => e.ChassisNumber, "vehicles_chassis_number_key").IsUnique();

            entity.HasIndex(e => e.EngineNumber, "vehicles_engine_number_key").IsUnique();

            entity.HasIndex(e => e.TruckPlate, "vehicles_truck_plate_key").IsUnique();

            entity.Property(e => e.VehicleId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("vehicle_id");
            entity.Property(e => e.Brand)
                .HasMaxLength(50)
                .HasColumnName("brand");
            entity.Property(e => e.ChassisNumber)
                .HasMaxLength(50)
                .HasColumnName("chassis_number");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EngineNumber)
                .HasMaxLength(50)
                .HasColumnName("engine_number");
            entity.Property(e => e.ManufactureYear).HasColumnName("manufacture_year");
            entity.Property(e => e.MaxCbm)
                .HasPrecision(8, 2)
                .HasColumnName("max_cbm");
            entity.Property(e => e.MaxTemp)
                .HasPrecision(5, 2)
                .HasColumnName("max_temp");
            entity.Property(e => e.MaxWeight)
                .HasPrecision(10, 2)
                .HasColumnName("max_weight");
            entity.Property(e => e.MinTemp)
                .HasPrecision(5, 2)
                .HasColumnName("min_temp");
            entity.Property(e => e.StandardFuelLiters)
                .HasPrecision(5, 2)
                .HasColumnName("standard_fuel_liters");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TruckPlate)
                .HasMaxLength(20)
                .HasColumnName("truck_plate");
            entity.Property(e => e.VehicleType)
                .HasMaxLength(50)
                .HasColumnName("vehicle_type");
        });

        modelBuilder.Entity<VehicleDocument>(entity =>
        {
            entity.HasKey(e => e.DocId).HasName("vehicle_documents_pkey");

            entity.ToTable("vehicle_documents");

            entity.Property(e => e.DocId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("doc_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentNumber)
                .HasMaxLength(50)
                .HasColumnName("document_number");
            entity.Property(e => e.ExpireDate).HasColumnName("expire_date");
            entity.Property(e => e.ImageUrl)
                .HasMaxLength(255)
                .HasColumnName("image_url");
            entity.Property(e => e.IssueDate).HasColumnName("issue_date");
            entity.Property(e => e.Issuer)
                .HasMaxLength(150)
                .HasColumnName("issuer");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.VehicleId).HasColumnName("vehicle_id");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.VehicleDocuments)
                .HasForeignKey(d => d.VehicleId)
                .HasConstraintName("fk_vd_vehicles");
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasKey(e => e.WarehouseId).HasName("warehouses_pkey");

            entity.ToTable("warehouses");

            entity.Property(e => e.WarehouseId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("warehouse_id");
            entity.Property(e => e.Address)
                .HasMaxLength(100)
                .HasColumnName("address");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentPallets)
                .HasDefaultValue(0)
                .HasColumnName("current_pallets");
            entity.Property(e => e.MaxPallets).HasColumnName("max_pallets");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.WarehouseName)
                .HasMaxLength(100)
                .HasColumnName("warehouse_name");
        });

        modelBuilder.Entity<WarehouseReceipt>(entity =>
        {
            entity.HasKey(e => e.ReceiptId).HasName("warehouse_receipts_pkey");

            entity.ToTable("warehouse_receipts");

            entity.HasIndex(e => e.ReceiptCode, "warehouse_receipts_receipt_code_key").IsUnique();

            entity.Property(e => e.ReceiptId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("receipt_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DelivererName)
                .HasMaxLength(100)
                .HasColumnName("deliverer_name");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.PdfUrl)
                .HasMaxLength(255)
                .HasColumnName("pdf_url");
            entity.Property(e => e.Reason)
                .HasMaxLength(255)
                .HasColumnName("reason");
            entity.Property(e => e.ReceiptCode)
                .HasMaxLength(50)
                .HasColumnName("receipt_code");
            entity.Property(e => e.ReceiptType)
                .HasMaxLength(20)
                .HasColumnName("receipt_type");
            entity.Property(e => e.ReceiverId).HasColumnName("receiver_id");
            entity.Property(e => e.RecordedTemperature)
                .HasPrecision(5, 2)
                .HasColumnName("recorded_temperature");
            entity.Property(e => e.ReferenceDocNo)
                .HasMaxLength(100)
                .HasColumnName("reference_doc_no");
            entity.Property(e => e.TotalActualQty)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("total_actual_qty");
            entity.Property(e => e.TotalExpectedQty)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("total_expected_qty");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.Order).WithMany(p => p.WarehouseReceipts)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_wr_to");

            entity.HasOne(d => d.Receiver).WithMany(p => p.WarehouseReceipts)
                .HasForeignKey(d => d.ReceiverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_wr_users");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.WarehouseReceipts)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_wr_wh");
        });

        modelBuilder.Entity<WarehouseReceiptItem>(entity =>
        {
            entity.HasKey(e => e.ItemId).HasName("warehouse_receipt_items_pkey");

            entity.ToTable("warehouse_receipt_items");

            entity.Property(e => e.ItemId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("item_id");
            entity.Property(e => e.ActualQty)
                .HasPrecision(10, 2)
                .HasColumnName("actual_qty");
            entity.Property(e => e.ConditionStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'GOOD'::character varying")
                .HasColumnName("condition_status");
            entity.Property(e => e.ExpectedQty)
                .HasPrecision(10, 2)
                .HasColumnName("expected_qty");
            entity.Property(e => e.ItemCode)
                .HasMaxLength(50)
                .HasColumnName("item_code");
            entity.Property(e => e.ItemName)
                .HasMaxLength(255)
                .HasColumnName("item_name");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.Unit)
                .HasMaxLength(20)
                .HasColumnName("unit");

            entity.HasOne(d => d.Receipt).WithMany(p => p.WarehouseReceiptItems)
                .HasForeignKey(d => d.ReceiptId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_wri_wr");
        });

        SeedLtlReferenceData(modelBuilder);

        OnModelCreatingPartial(modelBuilder);
    }

    private static void SeedLtlReferenceData(ModelBuilder modelBuilder)
    {
        var hcmDakLak = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var hcmCanTho = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var hcmDaNang = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var hcmHaNoi = Guid.Parse("10000000-0000-0000-0000-000000000004");

        modelBuilder.Entity<SystemConfig>().HasData(
            new SystemConfig
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Key = "PricePerKm",
                Value = "15000",
                Description = "Last-mile surcharge price per kilometer"
            },
            new SystemConfig
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                Key = "VolumetricConversionRate",
                Value = "250",
                Description = "CBM to volumetric kilogram conversion rate"
            });

        modelBuilder.Entity<RouteMaster>().HasData(
            new RouteMaster { RouteId = hcmDakLak, RouteCode = "HCM-DAKLAK", OriginCity = "HCM", DestCity = "Dak Lak", TransitTime = "1 - 1.5 ngay", CutOffTime = new TimeSpan(17, 0, 0), Status = "ACTIVE" },
            new RouteMaster { RouteId = hcmCanTho, RouteCode = "HCM-CANTHO", OriginCity = "HCM", DestCity = "Can Tho", TransitTime = "1 ngay", CutOffTime = new TimeSpan(18, 0, 0), Status = "ACTIVE" },
            new RouteMaster { RouteId = hcmDaNang, RouteCode = "HCM-DANANG", OriginCity = "HCM", DestCity = "Da Nang", TransitTime = "2 - 3 ngay", CutOffTime = new TimeSpan(16, 0, 0), Status = "ACTIVE" },
            new RouteMaster { RouteId = hcmHaNoi, RouteCode = "HCM-HANOI", OriginCity = "HCM", DestCity = "Ha Noi", TransitTime = "3 - 4 ngay", CutOffTime = new TimeSpan(15, 0, 0), Status = "ACTIVE" });

        modelBuilder.Entity<WeightTier>().HasData(
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000001"), RouteId = hcmHaNoi, MinWeightKg = 30, MaxWeightKg = 100, PricePerKg = 9000 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000002"), RouteId = hcmHaNoi, MinWeightKg = 100, MaxWeightKg = 500, PricePerKg = 7500 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000003"), RouteId = hcmHaNoi, MinWeightKg = 500, MaxWeightKg = 1000, PricePerKg = 6000 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000004"), RouteId = hcmHaNoi, MinWeightKg = 1000, MaxWeightKg = 1500, PricePerKg = 5000 },

            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000005"), RouteId = hcmDaNang, MinWeightKg = 30, MaxWeightKg = 100, PricePerKg = 7000 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000006"), RouteId = hcmDaNang, MinWeightKg = 100, MaxWeightKg = 500, PricePerKg = 5500 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000007"), RouteId = hcmDaNang, MinWeightKg = 500, MaxWeightKg = 1000, PricePerKg = 4000 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000008"), RouteId = hcmDaNang, MinWeightKg = 1000, MaxWeightKg = 1500, PricePerKg = 3500 },

            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000009"), RouteId = hcmCanTho, MinWeightKg = 30, MaxWeightKg = 100, PricePerKg = 4500 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000010"), RouteId = hcmCanTho, MinWeightKg = 100, MaxWeightKg = 500, PricePerKg = 3500 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000011"), RouteId = hcmCanTho, MinWeightKg = 500, MaxWeightKg = 1000, PricePerKg = 2500 },
            new WeightTier { Id = Guid.Parse("30000000-0000-0000-0000-000000000012"), RouteId = hcmCanTho, MinWeightKg = 1000, MaxWeightKg = 1500, PricePerKg = 2000 });
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
