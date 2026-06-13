using System;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260613000100_AddRouteOptionsAsnAndPricingAudit")]
    public partial class AddRouteOptionsAsnAndPricingAudit : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "route_master",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    route_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    origin_city = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    dest_city = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    etd = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    transit_time_hours = table.Column<int>(type: "integer", nullable: false),
                    cut_off_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("route_master_pkey", x => x.route_id);
                });

            migrationBuilder.CreateIndex(
                name: "route_master_route_code_key",
                table: "route_master",
                column: "route_code",
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "route_id",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_value",
                table: "pricing_matrix",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_value",
                table: "pricing_matrix",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_charge",
                table: "pricing_matrix",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "system_base_freight",
                table: "quotations",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "manual_adjustment",
                table: "quotations",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "override_reason",
                table: "quotations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pricing_source",
                table: "quotations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValueSql: "'AUTO'::character varying");

            migrationBuilder.CreateTable(
                name: "inbound_asn",
                columns: table => new
                {
                    asn_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    asn_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_dropoff_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    qr_code_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValueSql: "'SCHEDULED'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inbound_asn_pkey", x => x.asn_id);
                    table.ForeignKey(
                        name: "fk_asn_order",
                        column: x => x.order_id,
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "inbound_asn_asn_code_key",
                table: "inbound_asn",
                column: "asn_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_asn_order_id",
                table: "inbound_asn",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_transport_orders_route_id",
                table: "transport_orders",
                column: "route_id");

            migrationBuilder.AddForeignKey(
                name: "fk_to_route",
                table: "transport_orders",
                column: "route_id",
                principalTable: "route_master",
                principalColumn: "route_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_to_route",
                table: "transport_orders");

            migrationBuilder.DropTable(
                name: "inbound_asn");

            migrationBuilder.DropTable(
                name: "route_master");

            migrationBuilder.DropIndex(
                name: "ix_transport_orders_route_id",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "route_id",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "min_value",
                table: "pricing_matrix");

            migrationBuilder.DropColumn(
                name: "max_value",
                table: "pricing_matrix");

            migrationBuilder.DropColumn(
                name: "min_charge",
                table: "pricing_matrix");

            migrationBuilder.DropColumn(
                name: "system_base_freight",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "manual_adjustment",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "override_reason",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "pricing_source",
                table: "quotations");
        }
    }
}
