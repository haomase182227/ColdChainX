using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceAndAttachmentModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_location_item_batch",
                schema: "public",
                table: "inventory_stocks");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:attachment_category", "operational,compliance,quality,incident,disposal,evidence")
                .Annotation("Npgsql:Enum:attachment_format", "image,pdf,document")
                .Annotation("Npgsql:Enum:attachment_sub_category", "delivery_note,packing_list,invoice,vat_invoice,warehouse_receipt_note,warehouse_issue_note,handover_report,food_safety_certificate,quarantine_certificate,coa_certificate,product_license,batch_release_certificate,customs_declaration,import_permit,certificate_of_origin,plant_quarantine_certificate,vietgap_certificate,qc_report,damage_report,temperature_log,temperature_exception_report,dispute_report,disposal_report,destruction_certificate,vehicle_photo,seal_photo,temperature_photo,goods_condition_photo,damage_photo,barcode_photo,batch_photo,expiry_date_photo,handover_photo")
                .Annotation("Npgsql:Enum:document_status", "not_required,pending,verified,rejected,expired")
                .Annotation("Npgsql:Enum:product_category", "food,seafood,agriculture,pharma,vaccine,import_goods")
                .Annotation("Npgsql:Enum:requirement_level", "mandatory,conditional,optional");

            migrationBuilder.Sql(
                "UPDATE public.warehouse_receipt_items " +
                "SET batch_number = 'NO-BATCH' " +
                "WHERE batch_number IS NULL;"
            );

            migrationBuilder.AlterColumn<string>(
                name: "batch_number",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "NO-BATCH",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country_of_origin",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Vietnam");

            migrationBuilder.AddColumn<int>(
                name: "product_category",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 1. Add customer_id as nullable
            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                schema: "public",
                table: "inventory_stocks",
                type: "uuid",
                nullable: true);

            // 2. Populate existing rows using a valid system customer
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    default_cust_id UUID;
                BEGIN
                    -- Check if any customer exists
                    SELECT customer_id INTO default_cust_id FROM public.customers LIMIT 1;
                    
                    -- If no customer exists, create a default one
                    IF default_cust_id IS NULL THEN
                        default_cust_id := '00000000-0000-0000-0000-000000000001';
                        INSERT INTO public.customers (customer_id, company_name, tax_code, status, payment_term)
                        VALUES (default_cust_id, 'Default 3PL Customer', 'DEFAULT-3PL', 'ACTIVE', 30);
                    END IF;
                    
                    -- Update all inventory stocks with the default customer id
                    UPDATE public.inventory_stocks SET customer_id = default_cust_id WHERE customer_id IS NULL;
                END $$;
            ");

            // 3. Alter column to NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "customer_id",
                schema: "public",
                table: "inventory_stocks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_receipt_item_id",
                schema: "public",
                table: "inventory_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "compliance_zoning_rules",
                schema: "public",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    product_category = table.Column<int>(type: "integer", nullable: false),
                    sub_category = table.Column<int>(type: "integer", nullable: false),
                    requirement_level = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("compliance_zoning_rules_pkey", x => x.rule_id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_orders",
                schema: "public",
                columns: table => new
                {
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    receiver_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destination_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "DRAFT"),
                    assigned_picker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("outbound_orders_pkey", x => x.outbound_order_id);
                    table.ForeignKey(
                        name: "fk_outbound_customer",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outbound_picker",
                        column: x => x.assigned_picker_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbound_order_items",
                schema: "public",
                columns: table => new
                {
                    outbound_order_item_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("outbound_order_items_pkey", x => x.outbound_order_item_id);
                    table.ForeignKey(
                        name: "fk_item_outbound_order",
                        column: x => x.outbound_order_id,
                        principalSchema: "public",
                        principalTable: "outbound_orders",
                        principalColumn: "outbound_order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_evidence_attachments",
                schema: "public",
                columns: table => new
                {
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    file_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    sub_category = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issuer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    captured_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    seal_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    warehouse_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_receipt_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouse_evidence_attachments_pkey", x => x.attachment_id);
                    table.CheckConstraint("chk_attachment_target", "(warehouse_receipt_id IS NOT NULL)::int + (warehouse_receipt_item_id IS NOT NULL)::int + (inventory_adjustment_id IS NOT NULL)::int + (outbound_order_id IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "fk_att_adjustment",
                        column: x => x.inventory_adjustment_id,
                        principalSchema: "public",
                        principalTable: "inventory_adjustments",
                        principalColumn: "adjustment_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_att_outbound",
                        column: x => x.outbound_order_id,
                        principalSchema: "public",
                        principalTable: "outbound_orders",
                        principalColumn: "outbound_order_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_att_receipt",
                        column: x => x.warehouse_receipt_id,
                        principalSchema: "public",
                        principalTable: "warehouse_receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_att_receipt_item",
                        column: x => x.warehouse_receipt_item_id,
                        principalSchema: "public",
                        principalTable: "warehouse_receipt_items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "attachment_audit_history",
                schema: "public",
                columns: table => new
                {
                    history_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<int>(type: "integer", nullable: true),
                    new_status = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("attachment_audit_history_pkey", x => x.history_id);
                    table.ForeignKey(
                        name: "fk_history_attachment",
                        column: x => x.attachment_id,
                        principalSchema: "public",
                        principalTable: "warehouse_evidence_attachments",
                        principalColumn: "attachment_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "compliance_zoning_rules",
                columns: new[] { "rule_id", "created_at", "created_by", "is_active", "product_category", "requirement_level", "sub_category", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 0, 0, 7, null, null },
                    { new Guid("b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a2"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 0, 0, 24, null, null },
                    { new Guid("b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a3"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 0, 0, 26, null, null },
                    { new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a1"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 1, 0, 7, null, null },
                    { new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a2"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 1, 0, 8, null, null },
                    { new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a3"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 1, 0, 24, null, null },
                    { new Guid("b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a4"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 1, 0, 26, null, null },
                    { new Guid("b003a3a3-a3a3-a3a3-a3a3-a3a3a3a3a3a1"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 2, 0, 15, null, null },
                    { new Guid("b003a3a3-a3a3-a3a3-a3a3-a3a3a3a3a3a2"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 2, 0, 24, null, null },
                    { new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a1"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 3, 0, 10, null, null },
                    { new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a2"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 3, 0, 9, null, null },
                    { new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a3"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 3, 0, 24, null, null },
                    { new Guid("b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a4"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 3, 0, 26, null, null },
                    { new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a1"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 4, 0, 10, null, null },
                    { new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a2"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 4, 0, 11, null, null },
                    { new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a3"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 4, 0, 9, null, null },
                    { new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a4"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 4, 0, 24, null, null },
                    { new Guid("b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a5"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 4, 0, 26, null, null },
                    { new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a1"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 5, 0, 12, null, null },
                    { new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a2"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 5, 0, 14, null, null },
                    { new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a3"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 5, 1, 25, null, null },
                    { new Guid("b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a4"), new DateTime(2026, 6, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("00000000-0000-0000-0000-000000000000"), true, 5, 0, 24, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_stocks_customer_id",
                schema: "public",
                table: "inventory_stocks",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "uq_location_customer_item_batch",
                schema: "public",
                table: "inventory_stocks",
                columns: new[] { "location_id", "customer_id", "item_code", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_warehouse_receipt_item_id",
                schema: "public",
                table: "inventory_movements",
                column: "warehouse_receipt_item_id");

            migrationBuilder.CreateIndex(
                name: "idx_history_attachment",
                schema: "public",
                table: "attachment_audit_history",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "uq_rule_category_subcategory",
                schema: "public",
                table: "compliance_zoning_rules",
                columns: new[] { "product_category", "sub_category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbound_order_items_outbound_order_id",
                schema: "public",
                table: "outbound_order_items",
                column: "outbound_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_orders_assigned_picker_id",
                schema: "public",
                table: "outbound_orders",
                column: "assigned_picker_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_orders_customer_id",
                schema: "public",
                table: "outbound_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "uq_outbound_order_code",
                schema: "public",
                table: "outbound_orders",
                column: "order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_att_adjustment",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "inventory_adjustment_id");

            migrationBuilder.CreateIndex(
                name: "idx_att_outbound",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "outbound_order_id");

            migrationBuilder.CreateIndex(
                name: "idx_att_receipt",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "warehouse_receipt_id");

            migrationBuilder.CreateIndex(
                name: "idx_att_receipt_item",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "warehouse_receipt_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_movement_receipt_item",
                schema: "public",
                table: "inventory_movements",
                column: "warehouse_receipt_item_id",
                principalSchema: "public",
                principalTable: "warehouse_receipt_items",
                principalColumn: "item_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_customer",
                schema: "public",
                table: "inventory_stocks",
                column: "customer_id",
                principalSchema: "public",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_movement_receipt_item",
                schema: "public",
                table: "inventory_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_customer",
                schema: "public",
                table: "inventory_stocks");

            migrationBuilder.DropTable(
                name: "attachment_audit_history",
                schema: "public");

            migrationBuilder.DropTable(
                name: "compliance_zoning_rules",
                schema: "public");

            migrationBuilder.DropTable(
                name: "outbound_order_items",
                schema: "public");

            migrationBuilder.DropTable(
                name: "warehouse_evidence_attachments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "outbound_orders",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_inventory_stocks_customer_id",
                schema: "public",
                table: "inventory_stocks");

            migrationBuilder.DropIndex(
                name: "uq_location_customer_item_batch",
                schema: "public",
                table: "inventory_stocks");

            migrationBuilder.DropIndex(
                name: "IX_inventory_movements_warehouse_receipt_item_id",
                schema: "public",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "country_of_origin",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "product_category",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "public",
                table: "inventory_stocks");

            migrationBuilder.DropColumn(
                name: "warehouse_receipt_item_id",
                schema: "public",
                table: "inventory_movements");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:Enum:attachment_category", "operational,compliance,quality,incident,disposal,evidence")
                .OldAnnotation("Npgsql:Enum:attachment_format", "image,pdf,document")
                .OldAnnotation("Npgsql:Enum:attachment_sub_category", "delivery_note,packing_list,invoice,vat_invoice,warehouse_receipt_note,warehouse_issue_note,handover_report,food_safety_certificate,quarantine_certificate,coa_certificate,product_license,batch_release_certificate,customs_declaration,import_permit,certificate_of_origin,plant_quarantine_certificate,vietgap_certificate,qc_report,damage_report,temperature_log,temperature_exception_report,dispute_report,disposal_report,destruction_certificate,vehicle_photo,seal_photo,temperature_photo,goods_condition_photo,damage_photo,barcode_photo,batch_photo,expiry_date_photo,handover_photo")
                .OldAnnotation("Npgsql:Enum:document_status", "not_required,pending,verified,rejected,expired")
                .OldAnnotation("Npgsql:Enum:product_category", "food,seafood,agriculture,pharma,vaccine,import_goods")
                .OldAnnotation("Npgsql:Enum:requirement_level", "mandatory,conditional,optional");

            migrationBuilder.AlterColumn<string>(
                name: "batch_number",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "uq_location_item_batch",
                schema: "public",
                table: "inventory_stocks",
                columns: new[] { "location_id", "item_code", "batch_id" },
                unique: true);
        }
    }
}
