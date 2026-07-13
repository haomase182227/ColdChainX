using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStopSequenceFromRouteStops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) { }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transport_orders_route_master_RouteMasterRouteId",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_transport_orders_dropoff_stop",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_transport_orders_schedule",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_dropoff_stop_id",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_RouteMasterRouteId",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "RouteMasterRouteId",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "dropoff_stop_id",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.RenameColumn(
                name: "schedule_id",
                schema: "public",
                table: "transport_orders",
                newName: "route_id");

            migrationBuilder.RenameIndex(
                name: "IX_transport_orders_schedule_id",
                schema: "public",
                table: "transport_orders",
                newName: "IX_transport_orders_route_id");

            migrationBuilder.AddColumn<int>(
                name: "stop_sequence",
                schema: "public",
                table: "route_stops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "fk_transport_orders_route_master",
                schema: "public",
                table: "transport_orders",
                column: "route_id",
                principalSchema: "public",
                principalTable: "route_master",
                principalColumn: "route_id");
        }
    }
}


