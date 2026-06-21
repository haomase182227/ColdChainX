using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUltraFlatWarehouseLpnFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lpns",
                schema: "public",
                columns: table => new
                {
                    lpn_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    lpn_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    item_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    batch_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    storage_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    expected_weight_kg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expected_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actual_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    required_temperature = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    recorded_temperature = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    max_diff_percent = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    discrepancy_reason = table.Column<string>(type: "text", nullable: true),
                    grn_pdf_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    discrepancy_pdf_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    return_pdf_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    inbound_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    picked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    shipped_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    sla_deadline = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("lpns_pkey", x => x.lpn_id);
                    table.ForeignKey(
                        name: "fk_lpns_customer",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_lpns_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lpns_route",
                        column: x => x.route_id,
                        principalSchema: "public",
                        principalTable: "route_master",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_lpns_trip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "penalty_bills",
                schema: "public",
                columns: table => new
                {
                    penalty_bill_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    bill_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    lpn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handling_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    storage_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    paid_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    paid_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("penalty_bills_pkey", x => x.penalty_bill_id);
                    table.ForeignKey(
                        name: "fk_penalty_bills_customer",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_penalty_bills_lpn",
                        column: x => x.lpn_id,
                        principalSchema: "public",
                        principalTable: "lpns",
                        principalColumn: "lpn_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_penalty_bills_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_penalty_bills_paid_by",
                        column: x => x.paid_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_lpns_order_id",
                schema: "public",
                table: "lpns",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "idx_lpns_state",
                schema: "public",
                table: "lpns",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "idx_lpns_storage_location",
                schema: "public",
                table: "lpns",
                column: "storage_location");

            migrationBuilder.CreateIndex(
                name: "IX_lpns_customer_id",
                schema: "public",
                table: "lpns",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_lpns_route_id",
                schema: "public",
                table: "lpns",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_lpns_trip_id",
                schema: "public",
                table: "lpns",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "uq_lpns_lpn_code",
                schema: "public",
                table: "lpns",
                column: "lpn_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_penalty_bills_is_paid",
                schema: "public",
                table: "penalty_bills",
                column: "is_paid");

            migrationBuilder.CreateIndex(
                name: "idx_penalty_bills_lpn_id",
                schema: "public",
                table: "penalty_bills",
                column: "lpn_id");

            migrationBuilder.CreateIndex(
                name: "IX_penalty_bills_customer_id",
                schema: "public",
                table: "penalty_bills",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_penalty_bills_order_id",
                schema: "public",
                table: "penalty_bills",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_penalty_bills_paid_by",
                schema: "public",
                table: "penalty_bills",
                column: "paid_by");

            migrationBuilder.CreateIndex(
                name: "uq_penalty_bills_bill_code",
                schema: "public",
                table: "penalty_bills",
                column: "bill_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "penalty_bills",
                schema: "public");

            migrationBuilder.DropTable(
                name: "lpns",
                schema: "public");
        }
    }
}
