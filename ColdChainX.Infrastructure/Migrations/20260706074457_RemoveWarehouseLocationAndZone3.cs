using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWarehouseLocationAndZone3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "warehouse_locations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "warehouse_zones",
                schema: "public");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresInspection",
                schema: "public",
                table: "master_trips",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "advance_id",
                schema: "public",
                table: "expense_receipts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "TripId",
                schema: "public",
                table: "expense_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LpnId",
                schema: "public",
                table: "claims",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LpnId",
                schema: "public",
                table: "alert_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DetentionCharges",
                schema: "public",
                columns: table => new
                {
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StopId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    FreeMinutesAllocated = table.Column<int>(type: "integer", nullable: false),
                    ActualWaitMinutes = table.Column<int>(type: "integer", nullable: false),
                    AmountCharged = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetentionCharges", x => x.ChargeId);
                    table.ForeignKey(
                        name: "FK_DetentionCharges_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "FK_DetentionCharges_trip_stops_StopId",
                        column: x => x.StopId,
                        principalSchema: "public",
                        principalTable: "trip_stops",
                        principalColumn: "stop_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncidentEvidences",
                schema: "public",
                columns: table => new
                {
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceType = table.Column<string>(type: "text", nullable: false),
                    FileUrl = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentEvidences", x => x.EvidenceId);
                    table.ForeignKey(
                        name: "FK_IncidentEvidences_incident_reports_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "public",
                        principalTable: "incident_reports",
                        principalColumn: "incident_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripStopEvents",
                schema: "public",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    StopId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    EventTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MetaData = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStopEvents", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_TripStopEvents_trip_stops_StopId",
                        column: x => x.StopId,
                        principalSchema: "public",
                        principalTable: "trip_stops",
                        principalColumn: "stop_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expense_receipts_TripId",
                schema: "public",
                table: "expense_receipts",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_claims_LpnId",
                schema: "public",
                table: "claims",
                column: "LpnId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_logs_LpnId",
                schema: "public",
                table: "alert_logs",
                column: "LpnId");

            migrationBuilder.CreateIndex(
                name: "IX_DetentionCharges_CustomerId",
                schema: "public",
                table: "DetentionCharges",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DetentionCharges_StopId",
                schema: "public",
                table: "DetentionCharges",
                column: "StopId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvidences_IncidentId",
                schema: "public",
                table: "IncidentEvidences",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_TripStopEvents_StopId",
                schema: "public",
                table: "TripStopEvents",
                column: "StopId");

            migrationBuilder.AddForeignKey(
                name: "FK_alert_logs_lpns_LpnId",
                schema: "public",
                table: "alert_logs",
                column: "LpnId",
                principalSchema: "public",
                principalTable: "lpns",
                principalColumn: "lpn_id");

            migrationBuilder.AddForeignKey(
                name: "FK_claims_lpns_LpnId",
                schema: "public",
                table: "claims",
                column: "LpnId",
                principalSchema: "public",
                principalTable: "lpns",
                principalColumn: "lpn_id");

            migrationBuilder.AddForeignKey(
                name: "FK_expense_receipts_master_trips_TripId",
                schema: "public",
                table: "expense_receipts",
                column: "TripId",
                principalSchema: "public",
                principalTable: "master_trips",
                principalColumn: "trip_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_alert_logs_lpns_LpnId",
                schema: "public",
                table: "alert_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_claims_lpns_LpnId",
                schema: "public",
                table: "claims");

            migrationBuilder.DropForeignKey(
                name: "FK_expense_receipts_master_trips_TripId",
                schema: "public",
                table: "expense_receipts");

            migrationBuilder.DropTable(
                name: "DetentionCharges",
                schema: "public");

            migrationBuilder.DropTable(
                name: "IncidentEvidences",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TripStopEvents",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_expense_receipts_TripId",
                schema: "public",
                table: "expense_receipts");

            migrationBuilder.DropIndex(
                name: "IX_claims_LpnId",
                schema: "public",
                table: "claims");

            migrationBuilder.DropIndex(
                name: "IX_alert_logs_LpnId",
                schema: "public",
                table: "alert_logs");

            migrationBuilder.DropColumn(
                name: "RequiresInspection",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropColumn(
                name: "TripId",
                schema: "public",
                table: "expense_receipts");

            migrationBuilder.DropColumn(
                name: "LpnId",
                schema: "public",
                table: "claims");

            migrationBuilder.DropColumn(
                name: "LpnId",
                schema: "public",
                table: "alert_logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "advance_id",
                schema: "public",
                table: "expense_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "warehouse_zones",
                schema: "public",
                columns: table => new
                {
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    current_pallets = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    max_capacity_pallets = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'ACTIVE'::character varying"),
                    storage_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    temperature_max = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    temperature_min = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    zone_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    zone_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouse_zones_pkey", x => x.zone_id);
                    table.ForeignKey(
                        name: "fk_warehouse_zones_warehouses",
                        column: x => x.warehouse_id,
                        principalSchema: "public",
                        principalTable: "warehouses",
                        principalColumn: "warehouse_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_locations",
                schema: "public",
                columns: table => new
                {
                    location_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bay_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    current_pallets = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    level_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    location_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    max_capacity_pallets = table.Column<int>(type: "integer", nullable: false),
                    rack_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'ACTIVE'::character varying"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouse_locations_pkey", x => x.location_id);
                    table.ForeignKey(
                        name: "fk_warehouse_locations_zones",
                        column: x => x.zone_id,
                        principalSchema: "public",
                        principalTable: "warehouse_zones",
                        principalColumn: "zone_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_locations_zone_id_location_code",
                schema: "public",
                table: "warehouse_locations",
                columns: new[] { "zone_id", "location_code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_zones_warehouse_id_zone_code",
                schema: "public",
                table: "warehouse_zones",
                columns: new[] { "warehouse_id", "zone_code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");
        }
    }
}
