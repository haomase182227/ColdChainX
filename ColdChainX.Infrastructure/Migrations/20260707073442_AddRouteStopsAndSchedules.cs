using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteStopsAndSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cut_off_time",
                schema: "public",
                table: "route_master");

            migrationBuilder.AddColumn<DateTime>(
                name: "departure_date",
                schema: "public",
                table: "master_trips",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "route_id",
                schema: "public",
                table: "master_trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "schedule_id",
                schema: "public",
                table: "master_trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "route_schedules",
                schema: "public",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    departure_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    cut_off_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("route_schedules_pkey", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_routeschedule_route",
                        column: x => x.route_id,
                        principalSchema: "public",
                        principalTable: "route_master",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "route_stops",
                schema: "public",
                columns: table => new
                {
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stop_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    stop_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("route_stops_pkey", x => x.stop_id);
                    table.ForeignKey(
                        name: "fk_routestop_location",
                        column: x => x.location_id,
                        principalSchema: "public",
                        principalTable: "locations",
                        principalColumn: "location_id");
                    table.ForeignKey(
                        name: "fk_routestop_route",
                        column: x => x.route_id,
                        principalSchema: "public",
                        principalTable: "route_master",
                        principalColumn: "route_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_master_trips_route_id",
                schema: "public",
                table: "master_trips",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_master_trips_schedule_id",
                schema: "public",
                table: "master_trips",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "IX_route_schedules_route_id",
                schema: "public",
                table: "route_schedules",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "IX_route_stops_location_id",
                schema: "public",
                table: "route_stops",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_route_stops_route_id",
                schema: "public",
                table: "route_stops",
                column: "route_id");

            migrationBuilder.AddForeignKey(
                name: "fk_mtrip_route",
                schema: "public",
                table: "master_trips",
                column: "route_id",
                principalSchema: "public",
                principalTable: "route_master",
                principalColumn: "route_id");

            migrationBuilder.AddForeignKey(
                name: "fk_mtrip_schedule",
                schema: "public",
                table: "master_trips",
                column: "schedule_id",
                principalSchema: "public",
                principalTable: "route_schedules",
                principalColumn: "schedule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mtrip_route",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropForeignKey(
                name: "fk_mtrip_schedule",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropTable(
                name: "route_schedules",
                schema: "public");

            migrationBuilder.DropTable(
                name: "route_stops",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_master_trips_route_id",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropIndex(
                name: "IX_master_trips_schedule_id",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropColumn(
                name: "departure_date",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropColumn(
                name: "route_id",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropColumn(
                name: "schedule_id",
                schema: "public",
                table: "master_trips");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "cut_off_time",
                schema: "public",
                table: "route_master",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }
    }
}
