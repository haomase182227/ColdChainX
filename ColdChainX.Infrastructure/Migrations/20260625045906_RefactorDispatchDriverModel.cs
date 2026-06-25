using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorDispatchDriverModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── STEP 1: Add new column + tables BEFORE touching legacy columns ──
            migrationBuilder.AddColumn<decimal>(
                name: "estimated_duration_hours",
                schema: "public",
                table: "master_trips",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "driver_work_logs",
                schema: "public",
                columns: table => new
                {
                    work_log_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    driving_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("driver_work_logs_pkey", x => x.work_log_id);
                    table.ForeignKey(
                        name: "fk_driver_work_logs_driver",
                        column: x => x.driver_id,
                        principalSchema: "public",
                        principalTable: "drivers",
                        principalColumn: "driver_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_driver_work_logs_trip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "trip_drivers",
                schema: "public",
                columns: table => new
                {
                    trip_driver_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'PRIMARY'::character varying"),
                    assigned_duration_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("trip_drivers_pkey", x => x.trip_driver_id);
                    table.ForeignKey(
                        name: "fk_trip_drivers_driver",
                        column: x => x.driver_id,
                        principalSchema: "public",
                        principalTable: "drivers",
                        principalColumn: "driver_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_drivers_trip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_driver_work_logs_driver_date",
                schema: "public",
                table: "driver_work_logs",
                columns: new[] { "driver_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "IX_driver_work_logs_trip_id",
                schema: "public",
                table: "driver_work_logs",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_drivers_driver_id",
                schema: "public",
                table: "trip_drivers",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "trip_drivers_trip_driver_key",
                schema: "public",
                table: "trip_drivers",
                columns: new[] { "trip_id", "driver_id" },
                unique: true);

            // ── STEP 2: Migrate existing single-driver assignments into trip_drivers ──
            migrationBuilder.Sql(@"
                INSERT INTO public.trip_drivers (trip_driver_id, trip_id, driver_id, driver_role, assigned_duration_hours, created_at)
                SELECT gen_random_uuid(), mt.trip_id, mt.driver_id, 'PRIMARY', 0, CURRENT_TIMESTAMP
                FROM public.master_trips mt
                WHERE mt.driver_id IS NOT NULL
                  AND EXISTS (SELECT 1 FROM public.drivers d WHERE d.driver_id = mt.driver_id)
                  AND NOT EXISTS (
                      SELECT 1 FROM public.trip_drivers td
                      WHERE td.trip_id = mt.trip_id AND td.driver_id = mt.driver_id
                  );");

            // ── STEP 3: Drop legacy driver_id columns/relationships ──
            // Use idempotent DROP COLUMN ... IF EXISTS CASCADE so we tolerate schema drift
            // between environments (Azure may not have the EF-named FK/index on driver_id).
            // CASCADE removes any dependent FK constraint and index automatically.
            migrationBuilder.Sql("ALTER TABLE public.master_trips DROP COLUMN IF EXISTS driver_id CASCADE;");
            migrationBuilder.Sql("ALTER TABLE public.vehicles DROP COLUMN IF EXISTS driver_id CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── Re-add legacy columns ──
            migrationBuilder.AddColumn<Guid>(
                name: "driver_id",
                schema: "public",
                table: "vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "driver_id",
                schema: "public",
                table: "master_trips",
                type: "uuid",
                nullable: true);

            // ── Copy the PRIMARY driver of each trip back into master_trips.driver_id ──
            migrationBuilder.Sql(@"
                UPDATE public.master_trips mt
                SET driver_id = sub.driver_id
                FROM (
                    SELECT DISTINCT ON (trip_id) trip_id, driver_id
                    FROM public.trip_drivers
                    ORDER BY trip_id, (CASE WHEN driver_role = 'PRIMARY' THEN 0 ELSE 1 END), created_at
                ) sub
                WHERE mt.trip_id = sub.trip_id;");

            migrationBuilder.DropTable(
                name: "driver_work_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "trip_drivers",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "estimated_duration_hours",
                schema: "public",
                table: "master_trips");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_driver_id",
                schema: "public",
                table: "vehicles",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "IX_master_trips_driver_id",
                schema: "public",
                table: "master_trips",
                column: "driver_id");

            migrationBuilder.AddForeignKey(
                name: "fk_mtrip_drivers",
                schema: "public",
                table: "master_trips",
                column: "driver_id",
                principalSchema: "public",
                principalTable: "drivers",
                principalColumn: "driver_id");

            migrationBuilder.AddForeignKey(
                name: "fk_vehicles_drivers",
                schema: "public",
                table: "vehicles",
                column: "driver_id",
                principalSchema: "public",
                principalTable: "drivers",
                principalColumn: "driver_id");
        }
    }
}
