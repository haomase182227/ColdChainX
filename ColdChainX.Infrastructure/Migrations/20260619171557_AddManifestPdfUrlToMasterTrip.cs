using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManifestPdfUrlToMasterTrip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "route_id",
                schema: "public",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "additional_charges",
                schema: "public",
                table: "quotations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "chargeable_weight_kg",
                schema: "public",
                table: "quotations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "distance_km",
                schema: "public",
                table: "quotations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "manual_adjustment",
                schema: "public",
                table: "quotations",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true,
                defaultValueSql: "0");

            migrationBuilder.AddColumn<string>(
                name: "override_reason",
                schema: "public",
                table: "quotations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_per_kg",
                schema: "public",
                table: "quotations",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pricing_source",
                schema: "public",
                table: "quotations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AUTO");

            migrationBuilder.AddColumn<decimal>(
                name: "system_base_freight",
                schema: "public",
                table: "quotations",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_percentage",
                schema: "public",
                table: "quotations",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                defaultValue: 8m);

            migrationBuilder.AddColumn<decimal>(
                name: "volumetric_weight_kg",
                schema: "public",
                table: "quotations",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxValue",
                schema: "public",
                table: "pricing_matrix",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinCharge",
                schema: "public",
                table: "pricing_matrix",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinValue",
                schema: "public",
                table: "pricing_matrix",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "draft_html_content",
                schema: "public",
                table: "customer_contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "sent_at",
                schema: "public",
                table: "customer_contracts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signed_file_url",
                schema: "public",
                table: "customer_contracts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "uploaded_signed_at",
                schema: "public",
                table: "customer_contracts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "verified_at",
                schema: "public",
                table: "customer_contracts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "verified_by",
                schema: "public",
                table: "customer_contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_messages",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_messages_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_orders",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_messages_receiver",
                        column: x => x.receiver_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_messages_sender",
                        column: x => x.sender_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inbound_asns",
                schema: "public",
                columns: table => new
                {
                    asn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asn_code = table.Column<string>(type: "text", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_dropoff_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    qr_code_value = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inbound_asns_pkey", x => x.asn_id);
                    table.ForeignKey(
                        name: "fk_inbound_asns_orders",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "route_master",
                schema: "public",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_code = table.Column<string>(type: "text", nullable: false),
                    origin_city = table.Column<string>(type: "text", nullable: false),
                    dest_city = table.Column<string>(type: "text", nullable: false),
                    transit_time = table.Column<string>(type: "text", nullable: false),
                    cut_off_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("route_master_pkey", x => x.route_id);
                });

            migrationBuilder.CreateTable(
                name: "system_configs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("system_configs_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weight_tiers",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    max_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    price_per_kg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("weight_tiers_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fk_weight_tiers_route_master",
                        column: x => x.route_id,
                        principalSchema: "public",
                        principalTable: "route_master",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_route_id",
                schema: "public",
                table: "transport_orders",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_order_id",
                schema: "public",
                table: "chat_messages",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_receiver_id",
                schema: "public",
                table: "chat_messages",
                column: "receiver_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_sender_id",
                schema: "public",
                table: "chat_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_asns_order_id",
                schema: "public",
                table: "inbound_asns",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_weight_tiers_route_id",
                schema: "public",
                table: "weight_tiers",
                column: "route_id");

            migrationBuilder.AddForeignKey(
                name: "fk_transport_orders_route_master",
                schema: "public",
                table: "transport_orders",
                column: "route_id",
                principalSchema: "public",
                principalTable: "route_master",
                principalColumn: "route_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transport_orders_route_master",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropTable(
                name: "chat_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inbound_asns",
                schema: "public");

            migrationBuilder.DropTable(
                name: "system_configs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "weight_tiers",
                schema: "public");

            migrationBuilder.DropTable(
                name: "route_master",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_route_id",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "route_id",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "additional_charges",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "chargeable_weight_kg",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "distance_km",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "manual_adjustment",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "override_reason",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "price_per_kg",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "pricing_source",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "system_base_freight",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "vat_percentage",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "volumetric_weight_kg",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "MaxValue",
                schema: "public",
                table: "pricing_matrix");

            migrationBuilder.DropColumn(
                name: "MinCharge",
                schema: "public",
                table: "pricing_matrix");

            migrationBuilder.DropColumn(
                name: "MinValue",
                schema: "public",
                table: "pricing_matrix");

            migrationBuilder.DropColumn(
                name: "draft_html_content",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "sent_at",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "signed_file_url",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "uploaded_signed_at",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "verified_at",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "verified_by",
                schema: "public",
                table: "customer_contracts");
        }
    }
}
