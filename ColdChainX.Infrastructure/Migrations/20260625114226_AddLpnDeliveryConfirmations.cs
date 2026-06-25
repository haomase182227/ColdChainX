using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLpnDeliveryConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lpn_delivery_confirmations",
                schema: "public",
                columns: table => new
                {
                    confirmation_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    lpn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    receiver_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    receiver_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reject_note = table.Column<string>(type: "text", nullable: true),
                    evidence_image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("lpn_delivery_confirmations_pkey", x => x.confirmation_id);
                    table.ForeignKey(
                        name: "fk_lpn_delivery_confirmations_driver",
                        column: x => x.confirmed_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lpn_delivery_confirmations_lpn",
                        column: x => x.lpn_id,
                        principalSchema: "public",
                        principalTable: "lpns",
                        principalColumn: "lpn_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lpn_delivery_confirmations_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lpn_delivery_confirmations_trip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lpn_delivery_confirmations_confirmed_by",
                schema: "public",
                table: "lpn_delivery_confirmations",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "IX_lpn_delivery_confirmations_order_id",
                schema: "public",
                table: "lpn_delivery_confirmations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_lpn_delivery_confirmations_trip_id",
                schema: "public",
                table: "lpn_delivery_confirmations",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "uq_lpn_delivery_confirmations_lpn_id",
                schema: "public",
                table: "lpn_delivery_confirmations",
                column: "lpn_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lpn_delivery_confirmations",
                schema: "public");
        }
    }
}
