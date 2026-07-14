using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRouteScheduleDayOfWeekToDepartureDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "day_of_week",
                schema: "public",
                table: "route_schedules");

            migrationBuilder.AddColumn<DateTime>(
                name: "departure_date",
                schema: "public",
                table: "route_schedules",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "departure_date",
                schema: "public",
                table: "route_schedules");

            migrationBuilder.AddColumn<int>(
                name: "day_of_week",
                schema: "public",
                table: "route_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
