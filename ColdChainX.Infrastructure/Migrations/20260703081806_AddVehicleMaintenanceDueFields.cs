using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleMaintenanceDueFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "next_maintenance_date",
                schema: "public",
                table: "vehicles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "warning_days_before_due",
                schema: "public",
                table: "vehicles",
                type: "integer",
                nullable: false,
                defaultValueSql: "15");

            migrationBuilder.AddColumn<double>(
                name: "warning_km_before_due",
                schema: "public",
                table: "vehicles",
                type: "double precision",
                nullable: false,
                defaultValueSql: "500.0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "next_maintenance_date",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "warning_days_before_due",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "warning_km_before_due",
                schema: "public",
                table: "vehicles");
        }
    }
}
