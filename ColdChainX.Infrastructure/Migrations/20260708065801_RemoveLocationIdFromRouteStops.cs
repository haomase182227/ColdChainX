using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLocationIdFromRouteStops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) { }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                schema: "public",
                table: "route_stops",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_stops_location_id",
                schema: "public",
                table: "route_stops",
                column: "location_id");

            migrationBuilder.AddForeignKey(
                name: "fk_routestop_location",
                schema: "public",
                table: "route_stops",
                column: "location_id",
                principalSchema: "public",
                principalTable: "locations",
                principalColumn: "location_id");
        }
    }
}

