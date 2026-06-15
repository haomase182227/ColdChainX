using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleCountTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cycle_count_plans",
                schema: "public",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    plan_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true)
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
                name: "cycle_count_entries",
                schema: "public",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    system_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    system_pallets = table.Column<int>(type: "integer", nullable: false),
                    counted_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    counted_pallets = table.Column<int>(type: "integer", nullable: true),
                    variance_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    variance_pallets = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    counted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    counted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_notes = table.Column<string>(type: "text", nullable: true),
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: true)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cycle_count_entries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "cycle_count_plans",
                schema: "public");
        }
    }
}
