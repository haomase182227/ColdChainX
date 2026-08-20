using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexiblePackageLinesOrderFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cbm_estimation_confidence",
                schema: "public",
                table: "order_dimensions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cbm_estimation_method",
                schema: "public",
                table: "order_dimensions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "customer_provided_total_cbm",
                schema: "public",
                table: "order_dimensions",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_package_quantity",
                schema: "public",
                table: "order_dimensions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inbound_qc_package_lines",
                schema: "public",
                columns: table => new
                {
                    inbound_qc_package_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lpn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    actual_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inbound_qc_package_lines_pkey", x => x.inbound_qc_package_line_id);
                    table.ForeignKey(
                        name: "fk_inbound_qc_package_lines_asn",
                        column: x => x.asn_id,
                        principalSchema: "public",
                        principalTable: "inbound_asn",
                        principalColumn: "asn_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inbound_qc_package_lines_lpn",
                        column: x => x.lpn_id,
                        principalSchema: "public",
                        principalTable: "lpns",
                        principalColumn: "lpn_id");
                    table.ForeignKey(
                        name: "fk_inbound_qc_package_lines_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_package_lines",
                schema: "public",
                columns: table => new
                {
                    order_package_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    capacity_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_package_lines_pkey", x => x.order_package_line_id);
                    table.ForeignKey(
                        name: "fk_order_package_lines_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbound_qc_package_lines_asn_id",
                schema: "public",
                table: "inbound_qc_package_lines",
                column: "asn_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_qc_package_lines_lpn_id",
                schema: "public",
                table: "inbound_qc_package_lines",
                column: "lpn_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_qc_package_lines_order_id",
                schema: "public",
                table: "inbound_qc_package_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_package_lines_order_id",
                schema: "public",
                table: "order_package_lines",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_qc_package_lines",
                schema: "public");

            migrationBuilder.DropTable(
                name: "order_package_lines",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "cbm_estimation_confidence",
                schema: "public",
                table: "order_dimensions");

            migrationBuilder.DropColumn(
                name: "cbm_estimation_method",
                schema: "public",
                table: "order_dimensions");

            migrationBuilder.DropColumn(
                name: "customer_provided_total_cbm",
                schema: "public",
                table: "order_dimensions");

            migrationBuilder.DropColumn(
                name: "total_package_quantity",
                schema: "public",
                table: "order_dimensions");
        }
    }
}
