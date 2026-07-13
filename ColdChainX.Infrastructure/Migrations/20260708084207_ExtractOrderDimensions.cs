using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtractOrderDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // removed DropForeignKey fk_transport_orders_dropoff_stop

            // removed DropForeignKey fk_transport_orders_schedule

            migrationBuilder.DropColumn(
                name: "actual_cbm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "actual_weight_kg",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "expected_cbm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "expected_weight_kg",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "height_cm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "length_cm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "width_cm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.AddColumn<bool>(
                name: "has_strong_odor",
                schema: "public",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_stackable",
                schema: "public",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "compliance_risk_score",
                schema: "public",
                table: "customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "risk_flags",
                schema: "public",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_dimensions",
                schema: "public",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    expected_cbm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    actual_cbm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_dimensions_pkey", x => x.order_id);
                    table.ForeignKey(
                        name: "fk_order_dimensions_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // removed AddForeignKey fk_transport_orders_route_schedule

            // removed AddForeignKey fk_transport_orders_route_stop
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transport_orders_route_schedule",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_transport_orders_route_stop",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropTable(
                name: "order_dimensions",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "has_strong_odor",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "is_stackable",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "compliance_risk_score",
                schema: "public",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "risk_flags",
                schema: "public",
                table: "customers");

            migrationBuilder.AddColumn<decimal>(
                name: "actual_cbm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_weight_kg",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_cbm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_weight_kg",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "length_cm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddForeignKey(
                name: "fk_transport_orders_dropoff_stop",
                schema: "public",
                table: "transport_orders",
                column: "dropoff_stop_id",
                principalSchema: "public",
                principalTable: "route_stops",
                principalColumn: "stop_id");

            migrationBuilder.AddForeignKey(
                name: "fk_transport_orders_schedule",
                schema: "public",
                table: "transport_orders",
                column: "schedule_id",
                principalSchema: "public",
                principalTable: "route_schedules",
                principalColumn: "schedule_id");
        }
    }
}







