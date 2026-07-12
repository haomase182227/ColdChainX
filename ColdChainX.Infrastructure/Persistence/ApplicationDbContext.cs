using System;
using System.Collections.Generic;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Persistence;

public partial class ApplicationDbContext : DbContext, IApplicationDbContext
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

    public virtual DbSet<TripDriver> TripDrivers { get; set; }

    public virtual DbSet<DriverWorkLog> DriverWorkLogs { get; set; }

    public virtual DbSet<ExpenseAdvance> ExpenseAdvances { get; set; }

    public virtual DbSet<ExpenseReceipt> ExpenseReceipts { get; set; }

    public virtual DbSet<GeoFence> GeoFences { get; set; }

    public virtual DbSet<IncidentReport> IncidentReports { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceLine> InvoiceLines { get; set; }

    public virtual DbSet<TripStopEvent> TripStopEvents { get; set; }

    public virtual DbSet<DetentionCharge> DetentionCharges { get; set; }

    public virtual DbSet<IncidentEvidence> IncidentEvidences { get; set; }

    public virtual DbSet<InboundAsn> InboundAsns { get; set; }

    public virtual DbSet<IotDevice> IotDevices { get; set; }

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

    public virtual DbSet<RouteStop> RouteStops { get; set; }

    public virtual DbSet<RouteSchedule> RouteSchedules { get; set; }

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

    public virtual DbSet<WeightTier> WeightTiers { get; set; }

    public virtual DbSet<OutboundOrder> OutboundOrders { get; set; }

    public virtual DbSet<OutboundOrderItem> OutboundOrderItems { get; set; }

    public virtual DbSet<Lpn> Lpns { get; set; }

    public virtual DbSet<PenaltyBill> PenaltyBills { get; set; }

    public virtual DbSet<ContractAppendix> ContractAppendices { get; set; }

    public virtual DbSet<InboundReturnSlip> InboundReturnSlips { get; set; }

    public virtual DbSet<ComplianceZoningRule> ComplianceZoningRules { get; set; }

    public virtual DbSet<LpnDeliveryConfirmation> LpnDeliveryConfirmations { get; set; }




    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Set default schema to public for PostgreSQL
        modelBuilder.HasDefaultSchema("public");

        // PostgreSQL Enums Configuration
        modelBuilder.HasPostgresEnum<AttachmentFormat>();
        modelBuilder.HasPostgresEnum<AttachmentCategory>();
        modelBuilder.HasPostgresEnum<AttachmentSubCategory>();
        modelBuilder.HasPostgresEnum<ProductCategory>();
        modelBuilder.HasPostgresEnum<DocumentStatus>();
        modelBuilder.HasPostgresEnum<RequirementLevel>();

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("chat_messages_pkey");
            entity.ToTable("chat_messages", "public");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");
            entity.Property(e => e.ReceiverId).HasColumnName("receiver_id");
            entity.Property(e => e.MessageContent).HasColumnName("message_content");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.HasOne(d => d.Order).WithMany(p => p.ChatMessages).HasForeignKey(d => d.OrderId).HasConstraintName("fk_chat_order");
            entity.HasOne(d => d.Sender).WithMany().HasForeignKey(d => d.SenderId).HasConstraintName("fk_chat_sender");
            entity.HasOne(d => d.Receiver).WithMany().HasForeignKey(d => d.ReceiverId).HasConstraintName("fk_chat_receiver");
        });

        modelBuilder.Entity<InboundAsn>(entity =>
        {
            entity.HasKey(e => e.AsnId).HasName("inbound_asn_pkey");
            entity.ToTable("inbound_asn", "public");
            entity.Property(e => e.AsnId).HasColumnName("asn_id");
            entity.Property(e => e.AsnCode).HasColumnName("asn_code").HasMaxLength(50);
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Phone).HasMaxLength(50).HasColumnName("phone");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.FileUrl).HasMaxLength(500).HasColumnName("file_url");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.RequestedDropoffTime).HasColumnType("timestamp without time zone").HasColumnName("requested_dropoff_time");
            entity.Property(e => e.QrCodeValue).HasColumnName("qr_code_value").HasMaxLength(500);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasOne(d => d.Order).WithMany(p => p.InboundAsns).HasForeignKey(d => d.OrderId).HasConstraintName("fk_asn_order");
        });

        modelBuilder.Entity<RouteMaster>(entity =>
        {
            entity.HasKey(e => e.RouteId).HasName("route_master_pkey");
            entity.ToTable("route_master", "public");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.RouteCode).HasColumnName("route_code").HasMaxLength(50);
            entity.Property(e => e.OriginCity).HasColumnName("origin_city").HasMaxLength(50);
            entity.Property(e => e.DestCity).HasColumnName("dest_city").HasMaxLength(50);
            entity.Property(e => e.TransitTime).HasColumnName("transit_time").HasMaxLength(50);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        });

        modelBuilder.Entity<RouteStop>(entity =>
        {
            entity.HasKey(e => e.StopId).HasName("route_stops_pkey");
            entity.ToTable("route_stops", "public");
            entity.Property(e => e.StopId).HasColumnName("stop_id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.RouteId).HasColumnName("route_id");

            entity.Property(e => e.StopName).HasColumnName("stop_name").HasMaxLength(150);

            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.Route).WithMany(p => p.RouteStops)
                .HasForeignKey(d => d.RouteId)
                .HasConstraintName("fk_routestop_route");


        });

        modelBuilder.Entity<RouteSchedule>(entity =>
        {
            entity.HasKey(e => e.ScheduleId).HasName("route_schedules_pkey");
            entity.ToTable("route_schedules", "public");
            entity.Property(e => e.ScheduleId).HasColumnName("schedule_id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.ScheduleName).HasColumnName("schedule_name").HasMaxLength(100);
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.DepartureTime).HasColumnName("departure_time");
            entity.Property(e => e.CutOffTime).HasColumnName("cut_off_time");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.Route).WithMany(p => p.RouteSchedules)
                .HasForeignKey(d => d.RouteId)
                .HasConstraintName("fk_routeschedule_route");
        });

        modelBuilder.Entity<SystemConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("system_configs_pkey");
            entity.ToTable("system_configs", "public");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(100);
            entity.Property(e => e.Value).HasColumnName("value").HasMaxLength(255);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        });

        modelBuilder.Entity<WeightTier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("weight_tiers_pkey");
            entity.ToTable("weight_tiers", "public");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.MinWeightKg).HasPrecision(10, 2).HasColumnName("min_weight_kg");
            entity.Property(e => e.MaxWeightKg).HasPrecision(10, 2).HasColumnName("max_weight_kg");
            entity.Property(e => e.PricePerKg).HasPrecision(18, 2).HasColumnName("price_per_kg");
            entity.HasOne(d => d.Route).WithMany(p => p.WeightTiers).HasForeignKey(d => d.RouteId).HasConstraintName("fk_weight_tiers_route");
        });

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
            entity.Property(e => e.SentAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("sent_at");
            entity.Property(e => e.SignedDate).HasColumnName("signed_date");
            entity.Property(e => e.SignedFileUrl)
                .HasMaxLength(255)
                .HasColumnName("signed_file_url");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
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

            entity.Property(e => e.CodAmount)
                .HasPrecision(15, 2)
                .HasColumnName("cod_amount");
            entity.Property(e => e.CodAmountPaid)
                .HasPrecision(15, 2)
                .HasColumnName("cod_amount_paid");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(20)
                .HasColumnName("payment_method");
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(20)
                .HasColumnName("payment_status");
            entity.Property(e => e.PaymentEvidenceImageUrl)
                .HasMaxLength(255)
                .HasColumnName("payment_evidence_image_url");
            entity.Property(e => e.HandoverConfirmedAt)
                .HasColumnName("handover_confirmed_at");
            entity.Property(e => e.HandoverPdfUrl)
                .HasMaxLength(500)
                .HasColumnName("handover_pdf_url");
            entity.Property(e => e.PaymentConfirmedAt)
                .HasColumnName("payment_confirmed_at");

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
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.IdentityNumber)
                .HasMaxLength(30)
                .HasColumnName("identity_number");
            entity.Property(e => e.JoinDate).HasColumnName("join_date");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .HasColumnName("phone_number");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_drivers_users");
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
            entity.Property(e => e.DeviceCode)
                .HasMaxLength(100)
                .HasColumnName("device_code");
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

            entity.HasIndex(e => e.DeviceCode, "uq_iot_devices_device_code")
                .IsUnique()
                .HasFilter("device_code IS NOT NULL");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.IotDevices)
                .HasForeignKey(d => d.VehicleId)
                .HasConstraintName("fk_iot_vehicles");
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
            entity.Property(e => e.TriggeredAtOdometer).HasColumnName("triggered_at_odometer");
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
            entity.Property(e => e.OriginLocationId).HasColumnName("origin_location_id");
            entity.Property(e => e.EstimatedDurationHours)
                .HasPrecision(8, 2)
                .HasColumnName("estimated_duration_hours");
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
            entity.Property(e => e.SealNumber)
                .HasMaxLength(100)
                .HasColumnName("seal_number");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
            entity.Property(e => e.DepartureDate)
                .HasColumnType("date")
                .HasColumnName("departure_date");

            entity.HasOne(d => d.DestinationLocation).WithMany(p => p.MasterTripDestinationLocations)
                .HasForeignKey(d => d.DestinationLocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mtrip_dest");

            entity.HasOne(d => d.OriginLocation).WithMany(p => p.MasterTripOriginLocations)
                .HasForeignKey(d => d.OriginLocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_mtrip_orig");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.MasterTrips)
                .HasForeignKey(d => d.VehicleId)
                .HasConstraintName("fk_mtrip_vehicles");

            entity.HasOne(d => d.Route).WithMany(p => p.MasterTrips)
                .HasForeignKey(d => d.RouteId)
                .HasConstraintName("fk_mtrip_route");

            entity.HasOne(d => d.Schedule).WithMany(p => p.MasterTrips)
                .HasForeignKey(d => d.ScheduleId)
                .HasConstraintName("fk_mtrip_schedule");
        });

        modelBuilder.Entity<TripDriver>(entity =>
        {
            entity.HasKey(e => e.TripDriverId).HasName("trip_drivers_pkey");

            entity.ToTable("trip_drivers");

            entity.HasIndex(e => new { e.TripId, e.DriverId }, "trip_drivers_trip_driver_key").IsUnique();

            entity.Property(e => e.TripDriverId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("trip_driver_id");
            entity.Property(e => e.TripId).HasColumnName("trip_id");
            entity.Property(e => e.DriverId).HasColumnName("driver_id");
            entity.Property(e => e.DriverRole)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PRIMARY'::character varying")
                .HasColumnName("driver_role");
            entity.Property(e => e.AssignedDurationHours)
                .HasPrecision(8, 2)
                .HasColumnName("assigned_duration_hours");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Trip).WithMany(p => p.TripDrivers)
                .HasForeignKey(d => d.TripId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_trip_drivers_trip");

            entity.HasOne(d => d.Driver).WithMany(p => p.TripDrivers)
                .HasForeignKey(d => d.DriverId)
                .HasConstraintName("fk_trip_drivers_driver");
        });

        modelBuilder.Entity<DriverWorkLog>(entity =>
        {
            entity.HasKey(e => e.WorkLogId).HasName("driver_work_logs_pkey");

            entity.ToTable("driver_work_logs");

            entity.HasIndex(e => new { e.DriverId, e.WorkDate }, "ix_driver_work_logs_driver_date");

            entity.Property(e => e.WorkLogId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("work_log_id");
            entity.Property(e => e.DriverId).HasColumnName("driver_id");
            entity.Property(e => e.TripId).HasColumnName("trip_id");
            entity.Property(e => e.WorkDate).HasColumnName("work_date");
            entity.Property(e => e.DrivingHours)
                .HasPrecision(8, 2)
                .HasColumnName("driving_hours");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Driver).WithMany(p => p.WorkLogs)
                .HasForeignKey(d => d.DriverId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_driver_work_logs_driver");

            entity.HasOne(d => d.Trip).WithMany()
                .HasForeignKey(d => d.TripId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_driver_work_logs_trip");
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
            entity.Property(e => e.OriginCity)
                .HasMaxLength(50)
                .HasColumnName("origin_city");
            entity.Property(e => e.PricingUnit)
                .HasMaxLength(20)
                .HasColumnName("pricing_unit");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(15, 2)
                .HasColumnName("unit_price");
            entity.Property(e => e.MinValue)
                .HasPrecision(12, 4)
                .HasColumnName("min_value");
            entity.Property(e => e.MaxValue)
                .HasPrecision(12, 4)
                .HasColumnName("max_value");
            entity.Property(e => e.MinCharge)
                .HasPrecision(15, 2)
                .HasColumnName("min_charge");
        });

        modelBuilder.Entity<Quotation>(entity =>
        {
            entity.HasKey(e => e.QuoteId).HasName("quotations_pkey");

            entity.ToTable("quotations");

            entity.Property(e => e.QuoteId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("quote_id");
            entity.Property(e => e.AdditionalCharges)
                .HasColumnType("jsonb")
                .HasColumnName("additional_charges");
            entity.Property(e => e.BaseFreight)
                .HasPrecision(15, 2)
                .HasColumnName("base_freight");
            entity.Property(e => e.ChargeableWeightKg)
                .HasPrecision(10, 2)
                .HasColumnName("chargeable_weight_kg");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DistanceKm)
                .HasPrecision(10, 2)
                .HasColumnName("distance_km");
            entity.Property(e => e.FinalAmount)
                .HasPrecision(15, 2)
                .HasColumnName("final_amount");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(255)
                .HasColumnName("file_url");
            entity.Property(e => e.LastMileSurcharge)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("last_mile_surcharge");
            entity.Property(e => e.ManualAdjustment)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("manual_adjustment");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.OverrideReason)
                .HasMaxLength(500)
                .HasColumnName("override_reason");
            entity.Property(e => e.PricePerKg)
                .HasPrecision(15, 2)
                .HasColumnName("price_per_kg");
            entity.Property(e => e.PricingSource)
                .HasMaxLength(30)
                .HasDefaultValue("AUTO")
                .HasColumnName("pricing_source");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.SystemBaseFreight)
                .HasPrecision(15, 2)
                .HasColumnName("system_base_freight");
            entity.Property(e => e.VasAmount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("vas_amount");
            entity.Property(e => e.VatAmount)
                .HasPrecision(15, 2)
                .HasColumnName("vat_amount");
            entity.Property(e => e.VatPercentage)
                .HasPrecision(5, 2)
                .HasDefaultValue(8m)
                .HasColumnName("vat_percentage");
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
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
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
            entity.Property(e => e.Category)
                .HasMaxLength(50)
                .HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DestLocation).HasColumnName("dest_location");
            entity.Property(e => e.HasStrongOdor).HasColumnName("has_strong_odor");
            entity.Property(e => e.IsStackable).HasDefaultValue(true).HasColumnName("is_stackable");
            entity.Property(e => e.ItemName)
                .HasMaxLength(150)
                .HasColumnName("item_name");
            entity.Property(e => e.MasterTripId).HasColumnName("master_trip_id");
            entity.Property(e => e.PickupLocation).HasColumnName("pickup_location");
            entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
            entity.Property(e => e.DropoffStopId).HasColumnName("dropoff_stop_id");
            entity.Property(e => e.PackingType)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Thùng'::character varying")
                .HasColumnName("packing_type");
            entity.Property(e => e.Quantity)
                .HasDefaultValue(1)
                .HasColumnName("quantity");
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

            entity.HasOne(d => d.Schedule).WithMany()
                .HasForeignKey(d => d.ScheduleId)
                .HasConstraintName("fk_transport_orders_route_schedule");

            entity.HasOne(d => d.DropoffStop).WithMany()
                .HasForeignKey(d => d.DropoffStopId)
                .HasConstraintName("fk_transport_orders_route_stop");
        });

        modelBuilder.Entity<OrderDimension>(entity =>
        {
            entity.HasKey(e => e.OrderId).HasName("order_dimensions_pkey");

            entity.ToTable("order_dimensions");

            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.ExpectedWeightKg).HasPrecision(10, 2).HasColumnName("expected_weight_kg");
            entity.Property(e => e.ActualWeightKg).HasPrecision(10, 2).HasColumnName("actual_weight_kg");
            entity.Property(e => e.ExpectedCbm).HasPrecision(8, 2).HasColumnName("expected_cbm");
            entity.Property(e => e.ActualCbm).HasPrecision(8, 2).HasColumnName("actual_cbm");
            entity.Property(e => e.LengthCm).HasPrecision(10, 2).HasColumnName("length_cm");
            entity.Property(e => e.WidthCm).HasPrecision(10, 2).HasColumnName("width_cm");
            entity.Property(e => e.HeightCm).HasPrecision(10, 2).HasColumnName("height_cm");

            entity.HasOne(d => d.Order)
                .WithOne(p => p.OrderDimension)
                .HasForeignKey<OrderDimension>(d => d.OrderId)
                .HasConstraintName("fk_order_dimensions_order");
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
                .HasMaxLength(36)
                .HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
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
                .HasColumnType("text")
                .HasColumnName("refresh_token");
            entity.Property(e => e.RefreshTokenExpiryTime)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("refresh_token_expiry_time");
            entity.Property(e => e.RoleId)
                .HasColumnName("role_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedBy).HasColumnName("deleted_by");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");

            entity.Property(e => e.WarehouseId)
                .HasColumnName("warehouse_id");

            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("fk_users_roles");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.Users)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_users_warehouse");

            entity.HasQueryFilter(e => e.DeletedAt == null);
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
            entity.Property(e => e.CurrentLocation)
                .HasMaxLength(255)
                .HasColumnName("current_location");
            entity.Property(e => e.CurrentOdometer).HasColumnName("current_odometer");
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
            entity.Property(e => e.NextMaintenanceOdometer).HasColumnName("next_maintenance_odometer");
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
            entity.Property(e => e.DocumentType)
                .HasMaxLength(50)
                .HasColumnName("document_type");
            entity.Property(e => e.ExpireDate).HasColumnName("expire_date");
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

            entity.HasIndex(e => e.WarehouseCode, "warehouses_warehouse_code_key").IsUnique().HasFilter("\"deleted_at\" IS NULL");

            entity.Property(e => e.WarehouseId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("warehouse_id");
            entity.Property(e => e.WarehouseCode)
                .HasMaxLength(20)
                .HasColumnName("warehouse_code");
            entity.Property(e => e.WarehouseType)
                .HasMaxLength(20)
                .HasColumnName("warehouse_type");
            entity.Property(e => e.DefaultMinTemp)
                .HasPrecision(5, 2)
                .HasColumnName("default_min_temp");
            entity.Property(e => e.DefaultMaxTemp)
                .HasPrecision(5, 2)
                .HasColumnName("default_max_temp");
            entity.Property(e => e.Address)
                .HasMaxLength(100)
                .HasColumnName("address");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
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
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp with time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DeletedBy).HasColumnName("deleted_by");

            entity.HasQueryFilter(e => e.DeletedAt == null);
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

        modelBuilder.Entity<OutboundOrder>(entity =>
        {
            entity.HasKey(e => e.OutboundOrderId).HasName("outbound_orders_pkey");
            entity.ToTable("outbound_orders");

            entity.HasIndex(e => e.OrderCode, "uq_outbound_order_code").IsUnique();

            entity.Property(e => e.OutboundOrderId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("outbound_order_id");
            entity.Property(e => e.OrderCode)
                .HasMaxLength(50)
                .HasColumnName("order_code");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.ReceiverName)
                .HasMaxLength(100)
                .HasColumnName("receiver_name");
            entity.Property(e => e.ReceiverPhone)
                .HasMaxLength(20)
                .HasColumnName("receiver_phone");
            entity.Property(e => e.DestinationAddress)
                .HasMaxLength(255)
                .HasColumnName("destination_address");

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasDefaultValue(OutboundOrderStatus.DRAFT)
                .HasSentinel((OutboundOrderStatus)0)
                .HasColumnName("status");

            entity.Property(e => e.AssignedPickerId).HasColumnName("assigned_picker_id");
            entity.Property(e => e.AllocatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("allocated_at");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");

            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_outbound_customer");

            entity.HasOne(d => d.AssignedPicker).WithMany()
                .HasForeignKey(d => d.AssignedPickerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_outbound_picker");
        });

        modelBuilder.Entity<OutboundOrderItem>(entity =>
        {
            entity.HasKey(e => e.OutboundOrderItemId).HasName("outbound_order_items_pkey");
            entity.ToTable("outbound_order_items");

            entity.Property(e => e.OutboundOrderItemId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("outbound_order_item_id");
            entity.Property(e => e.OutboundOrderId).HasColumnName("outbound_order_id");
            entity.Property(e => e.ItemCode)
                .HasMaxLength(50)
                .HasColumnName("item_code");
            entity.Property(e => e.ItemName)
                .HasMaxLength(255)
                .HasColumnName("item_name");
            entity.Property(e => e.Unit)
                .HasMaxLength(20)
                .HasColumnName("unit");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 2)
                .HasColumnName("quantity");

            entity.HasOne(d => d.OutboundOrder).WithMany(p => p.OutboundOrderItems)
                .HasForeignKey(d => d.OutboundOrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_item_outbound_order");
        });



        modelBuilder.Entity<ComplianceZoningRule>(entity =>
        {
            entity.HasKey(e => e.RuleId).HasName("compliance_zoning_rules_pkey");
            entity.ToTable("compliance_zoning_rules");

            entity.Property(e => e.RuleId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("rule_id");

            entity.Property(e => e.ProductCategory)
                .HasColumnName("product_category");

            entity.Property(e => e.SubCategory)
                .HasColumnName("sub_category");

            entity.Property(e => e.RequirementLevel)
                .HasColumnName("requirement_level");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.Property(e => e.CreatedBy)
                .HasColumnName("created_by");

            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.Property(e => e.UpdatedBy)
                .HasColumnName("updated_by");

            entity.HasIndex(e => new { e.ProductCategory, e.SubCategory }, "uq_rule_category_subcategory").IsUnique();
        });

        modelBuilder.Entity<ComplianceZoningRule>().HasData(
            new ComplianceZoningRule { RuleId = new Guid("b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1"), ProductCategory = ProductCategory.FOOD, SubCategory = AttachmentSubCategory.FOOD_SAFETY_CERTIFICATE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a2"), ProductCategory = ProductCategory.FOOD, SubCategory = AttachmentSubCategory.VEHICLE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a3"), ProductCategory = ProductCategory.FOOD, SubCategory = AttachmentSubCategory.TEMPERATURE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },

            new ComplianceZoningRule { RuleId = new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a1"), ProductCategory = ProductCategory.SEAFOOD, SubCategory = AttachmentSubCategory.FOOD_SAFETY_CERTIFICATE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a2"), ProductCategory = ProductCategory.SEAFOOD, SubCategory = AttachmentSubCategory.QUARANTINE_CERTIFICATE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a3"), ProductCategory = ProductCategory.SEAFOOD, SubCategory = AttachmentSubCategory.VEHICLE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a4"), ProductCategory = ProductCategory.SEAFOOD, SubCategory = AttachmentSubCategory.TEMPERATURE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },

            new ComplianceZoningRule { RuleId = new Guid("b003a3a3-a3a3-a3a3-a3a3-a3a3a3a3a3a1"), ProductCategory = ProductCategory.AGRICULTURE, SubCategory = AttachmentSubCategory.PLANT_QUARANTINE_CERTIFICATE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b003a3a3-a3a3-a3a3-a3a3-a3a3a3a3a3a2"), ProductCategory = ProductCategory.AGRICULTURE, SubCategory = AttachmentSubCategory.VEHICLE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },

            new ComplianceZoningRule { RuleId = new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a1"), ProductCategory = ProductCategory.PHARMA, SubCategory = AttachmentSubCategory.PRODUCT_LICENSE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a2"), ProductCategory = ProductCategory.PHARMA, SubCategory = AttachmentSubCategory.COA_CERTIFICATE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a3"), ProductCategory = ProductCategory.PHARMA, SubCategory = AttachmentSubCategory.VEHICLE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a4"), ProductCategory = ProductCategory.PHARMA, SubCategory = AttachmentSubCategory.TEMPERATURE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },

            new ComplianceZoningRule { RuleId = new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a1"), ProductCategory = ProductCategory.VACCINE, SubCategory = AttachmentSubCategory.PRODUCT_LICENSE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a2"), ProductCategory = ProductCategory.VACCINE, SubCategory = AttachmentSubCategory.BATCH_RELEASE_CERTIFICATE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a3"), ProductCategory = ProductCategory.VACCINE, SubCategory = AttachmentSubCategory.COA_CERTIFICATE, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a4"), ProductCategory = ProductCategory.VACCINE, SubCategory = AttachmentSubCategory.VEHICLE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a5"), ProductCategory = ProductCategory.VACCINE, SubCategory = AttachmentSubCategory.TEMPERATURE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },

            new ComplianceZoningRule { RuleId = new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a1"), ProductCategory = ProductCategory.IMPORT_GOODS, SubCategory = AttachmentSubCategory.CUSTOMS_DECLARATION, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a2"), ProductCategory = ProductCategory.IMPORT_GOODS, SubCategory = AttachmentSubCategory.CERTIFICATE_OF_ORIGIN, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a3"), ProductCategory = ProductCategory.IMPORT_GOODS, SubCategory = AttachmentSubCategory.SEAL_PHOTO, RequirementLevel = RequirementLevel.CONDITIONAL, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty },
            new ComplianceZoningRule { RuleId = new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a4"), ProductCategory = ProductCategory.IMPORT_GOODS, SubCategory = AttachmentSubCategory.VEHICLE_PHOTO, RequirementLevel = RequirementLevel.MANDATORY, IsActive = true, CreatedAt = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Unspecified), CreatedBy = Guid.Empty }
        );

        modelBuilder.Entity<Lpn>(entity =>
        {
            entity.HasKey(e => e.LpnId).HasName("lpns_pkey");
            entity.ToTable("lpns");

            entity.Property(e => e.LpnId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("lpn_id");

            entity.Property(e => e.LpnCode)
                .HasMaxLength(50)
                .HasColumnName("lpn_code");

            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.TripId).HasColumnName("trip_id");

            entity.Property(e => e.StorageLocation)
                .HasMaxLength(200)
                .HasColumnName("storage_location");

            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.ActualWeightKg).HasPrecision(18, 2).HasColumnName("actual_weight_kg");
            entity.Property(e => e.ActualCbm).HasPrecision(18, 4).HasColumnName("actual_cbm");
            entity.Property(e => e.LengthCm).HasPrecision(10, 2).HasColumnName("length_cm");
            entity.Property(e => e.WidthCm).HasPrecision(10, 2).HasColumnName("width_cm");
            entity.Property(e => e.HeightCm).HasPrecision(10, 2).HasColumnName("height_cm");
            entity.Property(e => e.RequiredTemperature).HasPrecision(8, 2).HasColumnName("required_temperature");
            entity.Property(e => e.RecordedTemperature).HasPrecision(8, 2).HasColumnName("recorded_temperature");

            entity.Property(e => e.State)
                .HasConversion<string>()
                .HasMaxLength(30)
                .HasColumnName("state");

            entity.Property(e => e.DiscrepancyReason).HasColumnName("discrepancy_reason");
            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.EvidenceImageUrl).HasMaxLength(255).HasColumnName("evidence_image_url");
            entity.Property(e => e.InboundTime).HasColumnType("timestamp without time zone").HasColumnName("inbound_time");
            entity.Property(e => e.TripId).HasColumnName("trip_id");
            entity.Property(e => e.SlaDeadline).HasColumnType("timestamp without time zone").HasColumnName("sla_deadline");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");

            entity.HasIndex(e => e.LpnCode).IsUnique().HasDatabaseName("uq_lpns_lpn_code");
            entity.HasIndex(e => e.StorageLocation).HasDatabaseName("idx_lpns_storage_location");
            entity.HasIndex(e => e.State).HasDatabaseName("idx_lpns_state");
            entity.HasIndex(e => e.OrderId).HasDatabaseName("idx_lpns_order_id");
            entity.HasIndex(e => e.WarehouseId).HasDatabaseName("idx_lpns_warehouse_id");

            entity.HasOne(e => e.Order).WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_lpns_order");

            entity.HasOne(e => e.Customer).WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_lpns_customer");

            entity.HasOne(e => e.Route).WithMany()
                .HasForeignKey(e => e.RouteId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_lpns_route");

            entity.HasOne(e => e.Trip).WithMany()
                .HasForeignKey(e => e.TripId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_lpns_trip");

            entity.HasOne(e => e.Warehouse).WithMany(w => w.Lpns)
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_lpns_warehouse");
        });

        modelBuilder.Entity<PenaltyBill>(entity =>
        {
            entity.HasKey(e => e.PenaltyBillId).HasName("penalty_bills_pkey");
            entity.ToTable("penalty_bills");

            entity.Property(e => e.PenaltyBillId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("penalty_bill_id");

            entity.Property(e => e.BillCode)
                .HasMaxLength(50)
                .HasColumnName("bill_code");

            entity.Property(e => e.LpnId).HasColumnName("lpn_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.HandlingFee).HasPrecision(18, 2).HasColumnName("handling_fee");
            entity.Property(e => e.StorageFee).HasPrecision(18, 2).HasColumnName("storage_fee");
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2).HasColumnName("total_amount");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.IsPaid).HasColumnName("is_paid");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.PaidAt).HasColumnType("timestamp without time zone").HasColumnName("paid_at");
            entity.Property(e => e.PaidBy).HasColumnName("paid_by");

            entity.HasIndex(e => e.BillCode).IsUnique().HasDatabaseName("uq_penalty_bills_bill_code");
            entity.HasIndex(e => e.LpnId).HasDatabaseName("idx_penalty_bills_lpn_id");
            entity.HasIndex(e => e.IsPaid).HasDatabaseName("idx_penalty_bills_is_paid");

            entity.HasOne(e => e.Lpn).WithMany(e => e.PenaltyBills)
                .HasForeignKey(e => e.LpnId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_penalty_bills_lpn");

            entity.HasOne(e => e.Order).WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_penalty_bills_order");

            entity.HasOne(e => e.Customer).WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_penalty_bills_customer");

            entity.HasOne(e => e.PaidByNavigation).WithMany()
                .HasForeignKey(e => e.PaidBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_penalty_bills_paid_by");
        });

        modelBuilder.Entity<ContractAppendix>(entity =>
        {
            entity.HasKey(e => e.AppendixId).HasName("contract_appendices_pkey");
            entity.ToTable("contract_appendices");

            entity.Property(e => e.AppendixId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("appendix_id");

            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");

            entity.Property(e => e.AppendixNumber)
                .HasMaxLength(50)
                .HasColumnName("appendix_number");

            entity.Property(e => e.AdjustedPrice)
                .HasPrecision(18, 2)
                .HasColumnName("adjusted_price");

            entity.Property(e => e.Reason)
                .HasColumnName("reason");

            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");

            entity.Property(e => e.DraftHtmlContent)
                .HasColumnName("draft_html_content");

            entity.Property(e => e.PdfUrl)
                .HasMaxLength(255)
                .HasColumnName("pdf_url");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.Property(e => e.SentAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("sent_at");

            entity.Property(e => e.ResolvedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("resolved_at");

            entity.HasIndex(e => e.AppendixNumber).IsUnique().HasDatabaseName("uq_contract_appendices_number");

            entity.HasOne(e => e.Contract).WithMany()
                .HasForeignKey(e => e.ContractId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_contract_appendices_contract");

            entity.HasOne(e => e.Order).WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_contract_appendices_order");
        });

        modelBuilder.Entity<InboundReturnSlip>(entity =>
        {
            entity.HasKey(e => e.ReturnSlipId).HasName("inbound_return_slips_pkey");
            entity.ToTable("inbound_return_slips");

            entity.Property(e => e.ReturnSlipId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("return_slip_id");

            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.LpnId).HasColumnName("lpn_id");

            entity.Property(e => e.SlipCode)
                .HasMaxLength(50)
                .HasColumnName("slip_code");

            entity.Property(e => e.ReturnedWeightKg)
                .HasPrecision(18, 2)
                .HasColumnName("returned_weight_kg");

            entity.Property(e => e.ReturnedCbm)
                .HasPrecision(18, 4)
                .HasColumnName("returned_cbm");

            entity.Property(e => e.ReturnedQty)
                .HasColumnName("returned_qty");

            entity.Property(e => e.Reason)
                .HasColumnName("reason");

            entity.Property(e => e.PdfUrl)
                .HasMaxLength(255)
                .HasColumnName("pdf_url");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasIndex(e => e.SlipCode).IsUnique().HasDatabaseName("uq_inbound_return_slips_code");

            entity.HasOne(e => e.Order).WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_inbound_return_slips_order");

            entity.HasOne(e => e.Lpn).WithMany()
                .HasForeignKey(e => e.LpnId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_inbound_return_slips_lpn");
        });

        modelBuilder.Entity<LpnDeliveryConfirmation>(entity =>
        {
            entity.HasKey(e => e.ConfirmationId).HasName("lpn_delivery_confirmations_pkey");
            entity.ToTable("lpn_delivery_confirmations");

            entity.Property(e => e.ConfirmationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("confirmation_id");

            entity.Property(e => e.LpnId).HasColumnName("lpn_id");
            entity.Property(e => e.TripId).HasColumnName("trip_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");

            entity.Property(e => e.OutcomeType)
                .HasMaxLength(20)
                .HasColumnName("outcome_type");

            entity.Property(e => e.ReceiverName)
                .HasMaxLength(200)
                .HasColumnName("receiver_name");

            entity.Property(e => e.ReceiverPhone)
                .HasMaxLength(20)
                .HasColumnName("receiver_phone");

            entity.Property(e => e.RejectReason)
                .HasMaxLength(50)
                .HasColumnName("reject_reason");

            entity.Property(e => e.RejectNote)
                .HasColumnType("text")
                .HasColumnName("reject_note");

            entity.Property(e => e.EvidenceImageUrl)
                .HasMaxLength(500)
                .HasColumnName("evidence_image_url");

            entity.Property(e => e.ConfirmedByDriverId).HasColumnName("confirmed_by");

            entity.Property(e => e.ConfirmedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmed_at");

            entity.Property(e => e.CheckinAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("checkin_at");

            entity.Property(e => e.SignatureImageUrl)
                .HasMaxLength(500)
                .HasColumnName("signature_image_url");

            entity.Property(e => e.CodAmount)
                .HasPrecision(15, 2)
                .HasColumnName("cod_amount");

            entity.Property(e => e.CodPaymentMethod)
                .HasMaxLength(20)
                .HasColumnName("cod_payment_method");

            entity.Property(e => e.CodReceiptImageUrl)
                .HasMaxLength(500)
                .HasColumnName("cod_receipt_image_url");

            entity.Property(e => e.NewSealNumber)
                .HasMaxLength(50)
                .HasColumnName("new_seal_number");

            entity.Property(e => e.RecordedTemperature)
                .HasPrecision(8, 2)
                .HasColumnName("recorded_temperature");

            entity.Property(e => e.IsCodVerified)
                .HasDefaultValue(false)
                .HasColumnName("is_cod_verified");

            entity.Property(e => e.CodVerifiedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("cod_verified_at");

            entity.Property(e => e.CodVerifiedByUserId)
                .HasColumnName("cod_verified_by");

            entity.HasIndex(e => e.LpnId).IsUnique().HasDatabaseName("uq_lpn_delivery_confirmations_lpn_id");

            entity.HasOne(d => d.Lpn).WithMany()
                .HasForeignKey(d => d.LpnId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_lpn_delivery_confirmations_lpn");

            entity.HasOne(d => d.Trip).WithMany()
                .HasForeignKey(d => d.TripId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_lpn_delivery_confirmations_trip");

            entity.HasOne(d => d.Order).WithMany()
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_lpn_delivery_confirmations_order");

            entity.HasOne(d => d.ConfirmedByDriver).WithMany()
                .HasForeignKey(d => d.ConfirmedByDriverId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_lpn_delivery_confirmations_driver");

            entity.HasOne(d => d.CodVerifiedByUser).WithMany()
                .HasForeignKey(d => d.CodVerifiedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_lpn_delivery_confirmations_verified_by");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
