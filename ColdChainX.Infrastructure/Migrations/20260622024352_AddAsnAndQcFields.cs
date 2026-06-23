using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAsnAndQcFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // migrationBuilder.DropForeignKey(
            //     name: "fk_att_adjustment",
            //     schema: "public",
            //     table: "warehouse_evidence_attachments");

            // Already dropped in previous migration
            /*
            migrationBuilder.DropTable(
                name: "cycle_count_entries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_allocations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_holds",
                schema: "public");

            migrationBuilder.DropTable(
                name: "cycle_count_plans",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_adjustments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_stocks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_batches",
                schema: "public");
            */

            migrationBuilder.DropIndex(
                name: "idx_att_adjustment",
                schema: "public",
                table: "warehouse_evidence_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "chk_attachment_target",
                schema: "public",
                table: "warehouse_evidence_attachments");

            migrationBuilder.DropColumn(
                name: "inventory_adjustment_id",
                schema: "public",
                table: "warehouse_evidence_attachments");

            migrationBuilder.AddColumn<string>(
                name: "seal_number",
                schema: "public",
                table: "master_trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceImageUrl",
                schema: "public",
                table: "lpns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_item_id",
                schema: "public",
                table: "lpns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "public",
                table: "inbound_asn",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                schema: "public",
                table: "inbound_asn",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                schema: "public",
                table: "inbound_asn",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                schema: "public",
                table: "inbound_asn",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_attachment_target",
                schema: "public",
                table: "warehouse_evidence_attachments",
                sql: "(warehouse_receipt_id IS NOT NULL)::int + (warehouse_receipt_item_id IS NOT NULL)::int + (outbound_order_id IS NOT NULL)::int = 1");

            migrationBuilder.CreateIndex(
                name: "IX_lpns_receipt_item_id",
                schema: "public",
                table: "lpns",
                column: "receipt_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_lpns_receipt_item",
                schema: "public",
                table: "lpns",
                column: "receipt_item_id",
                principalSchema: "public",
                principalTable: "warehouse_receipt_items",
                principalColumn: "item_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lpns_receipt_item",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropCheckConstraint(
                name: "chk_attachment_target",
                schema: "public",
                table: "warehouse_evidence_attachments");

            migrationBuilder.DropIndex(
                name: "IX_lpns_receipt_item_id",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "seal_number",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropColumn(
                name: "EvidenceImageUrl",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "receipt_item_id",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "public",
                table: "inbound_asn");

            migrationBuilder.DropColumn(
                name: "FileUrl",
                schema: "public",
                table: "inbound_asn");

            migrationBuilder.DropColumn(
                name: "Phone",
                schema: "public",
                table: "inbound_asn");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                schema: "public",
                table: "inbound_asn");

            migrationBuilder.AddColumn<Guid>(
                name: "inventory_adjustment_id",
                schema: "public",
                table: "warehouse_evidence_attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cycle_count_plans",
                schema: "public",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    plan_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("cycle_count_plans_pkey", x => x.plan_id);
                    table.ForeignKey(
                        name: "fk_plan_assigned_user",
                        column: x => x.assigned_to_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_completer_user",
                        column: x => x.completed_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_creator_user",
                        column: x => x.created_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_warehouse",
                        column: x => x.warehouse_id,
                        principalSchema: "public",
                        principalTable: "warehouses",
                        principalColumn: "warehouse_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_batches",
                schema: "public",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    manufactured_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'ACTIVE'::character varying")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_batches_pkey", x => x.batch_id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "public",
                columns: table => new
                {
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_receipt_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    movement_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    reference_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_movements_pkey", x => x.movement_id);
                    table.ForeignKey(
                        name: "fk_movement_batch",
                        column: x => x.batch_id,
                        principalSchema: "public",
                        principalTable: "inventory_batches",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movement_from_loc",
                        column: x => x.from_location_id,
                        principalSchema: "public",
                        principalTable: "warehouse_locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movement_receipt_item",
                        column: x => x.warehouse_receipt_item_id,
                        principalSchema: "public",
                        principalTable: "warehouse_receipt_items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movement_to_loc",
                        column: x => x.to_location_id,
                        principalSchema: "public",
                        principalTable: "warehouse_locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_stocks",
                schema: "public",
                columns: table => new
                {
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    inbound_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    pallet_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    quantity_allocated = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "0.00"),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "0.00"),
                    required_temp_max = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    required_temp_min = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'AVAILABLE'::character varying"),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_stocks_pkey", x => x.stock_id);
                    table.CheckConstraint("CK_inventory_stocks_quantity_allocated_gte_zero", "quantity_allocated >= 0");
                    table.CheckConstraint("CK_inventory_stocks_quantity_on_hand_gte_zero", "quantity_on_hand >= 0");
                    table.ForeignKey(
                        name: "fk_stock_batch",
                        column: x => x.batch_id,
                        principalSchema: "public",
                        principalTable: "inventory_batches",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_customer",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_location",
                        column: x => x.location_id,
                        principalSchema: "public",
                        principalTable: "warehouse_locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_adjustments",
                schema: "public",
                columns: table => new
                {
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    pallets_after = table.Column<int>(type: "integer", nullable: false),
                    pallets_before = table.Column<int>(type: "integer", nullable: false),
                    pallets_changed = table.Column<int>(type: "integer", nullable: false),
                    quantity_after = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    quantity_before = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    quantity_changed = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    reason_notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PENDING_APPROVAL")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_adjustments_pkey", x => x.adjustment_id);
                    table.ForeignKey(
                        name: "fk_adj_movement",
                        column: x => x.movement_id,
                        principalSchema: "public",
                        principalTable: "inventory_movements",
                        principalColumn: "movement_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_adj_stock",
                        column: x => x.stock_id,
                        principalSchema: "public",
                        principalTable: "inventory_stocks",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_allocations",
                schema: "public",
                columns: table => new
                {
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocated_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'ALLOCATED'::character varying")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_allocations_pkey", x => x.allocation_id);
                    table.ForeignKey(
                        name: "fk_allocation_stock",
                        column: x => x.stock_id,
                        principalSchema: "public",
                        principalTable: "inventory_stocks",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cycle_count_entries",
                schema: "public",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    counted_pallets = table.Column<int>(type: "integer", nullable: true),
                    counted_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    manager_notes = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    system_pallets = table.Column<int>(type: "integer", nullable: false),
                    system_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    variance_pallets = table.Column<int>(type: "integer", nullable: true),
                    variance_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("cycle_count_entries_pkey", x => x.entry_id);
                    table.ForeignKey(
                        name: "fk_entry_adjustment",
                        column: x => x.adjustment_id,
                        principalSchema: "public",
                        principalTable: "inventory_adjustments",
                        principalColumn: "adjustment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entry_batch",
                        column: x => x.batch_id,
                        principalSchema: "public",
                        principalTable: "inventory_batches",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entry_counter_user",
                        column: x => x.counted_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entry_location",
                        column: x => x.location_id,
                        principalSchema: "public",
                        principalTable: "warehouse_locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entry_plan",
                        column: x => x.plan_id,
                        principalSchema: "public",
                        principalTable: "cycle_count_plans",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_entry_reviewer_user",
                        column: x => x.reviewed_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entry_stock",
                        column: x => x.stock_id,
                        principalSchema: "public",
                        principalTable: "inventory_stocks",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inventory_holds",
                schema: "public",
                columns: table => new
                {
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    hold_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    release_notes = table.Column<string>(type: "text", nullable: true),
                    released_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValueSql: "'HOLD'::character varying")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_holds_pkey", x => x.hold_id);
                    table.ForeignKey(
                        name: "fk_hold_adjustment",
                        column: x => x.adjustment_id,
                        principalSchema: "public",
                        principalTable: "inventory_adjustments",
                        principalColumn: "adjustment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hold_stock",
                        column: x => x.stock_id,
                        principalSchema: "public",
                        principalTable: "inventory_stocks",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hold_user_creator",
                        column: x => x.created_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hold_user_releaser",
                        column: x => x.released_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_att_adjustment",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "inventory_adjustment_id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_attachment_target",
                schema: "public",
                table: "warehouse_evidence_attachments",
                sql: "(warehouse_receipt_id IS NOT NULL)::int + (warehouse_receipt_item_id IS NOT NULL)::int + (inventory_adjustment_id IS NOT NULL)::int + (outbound_order_id IS NOT NULL)::int = 1");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_entries_adjustment_id",
                schema: "public",
                table: "cycle_count_entries",
                column: "adjustment_id");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_entries_batch_id",
                schema: "public",
                table: "cycle_count_entries",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_entries_counted_by",
                schema: "public",
                table: "cycle_count_entries",
                column: "counted_by");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_entries_location_id",
                schema: "public",
                table: "cycle_count_entries",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_entries_plan_id",
                schema: "public",
                table: "cycle_count_entries",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_entries_reviewed_by",
                schema: "public",
                table: "cycle_count_entries",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_entries_stock_id",
                schema: "public",
                table: "cycle_count_entries",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_plans_assigned_to_user_id",
                schema: "public",
                table: "cycle_count_plans",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_plans_completed_by",
                schema: "public",
                table: "cycle_count_plans",
                column: "completed_by");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_plans_created_by",
                schema: "public",
                table: "cycle_count_plans",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_count_plans_warehouse_id",
                schema: "public",
                table: "cycle_count_plans",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "uq_cycle_count_plan_code",
                schema: "public",
                table: "cycle_count_plans",
                column: "plan_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_movement_id",
                schema: "public",
                table: "inventory_adjustments",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_stock_id",
                schema: "public",
                table: "inventory_adjustments",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_stock_id",
                schema: "public",
                table: "inventory_allocations",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "uq_item_batch",
                schema: "public",
                table: "inventory_batches",
                columns: new[] { "item_code", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_adjustment_id",
                schema: "public",
                table: "inventory_holds",
                column: "adjustment_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_created_by",
                schema: "public",
                table: "inventory_holds",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_released_by",
                schema: "public",
                table: "inventory_holds",
                column: "released_by");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_stock_id",
                schema: "public",
                table: "inventory_holds",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_batch_id",
                schema: "public",
                table: "inventory_movements",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_from_location_id",
                schema: "public",
                table: "inventory_movements",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_to_location_id",
                schema: "public",
                table: "inventory_movements",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_warehouse_receipt_item_id",
                schema: "public",
                table: "inventory_movements",
                column: "warehouse_receipt_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_stocks_batch_id",
                schema: "public",
                table: "inventory_stocks",
                column: "batch_id");

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

            migrationBuilder.AddForeignKey(
                name: "fk_att_adjustment",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "inventory_adjustment_id",
                principalSchema: "public",
                principalTable: "inventory_adjustments",
                principalColumn: "adjustment_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
