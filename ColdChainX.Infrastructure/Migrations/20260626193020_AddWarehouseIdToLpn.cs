using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseIdToLpn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                schema: "public",
                table: "lpns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_lpns_warehouse_id",
                schema: "public",
                table: "lpns",
                column: "warehouse_id");

            migrationBuilder.AddForeignKey(
                name: "fk_lpns_warehouse",
                schema: "public",
                table: "lpns",
                column: "warehouse_id",
                principalSchema: "public",
                principalTable: "warehouses",
                principalColumn: "warehouse_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lpns_warehouse",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropIndex(
                name: "idx_lpns_warehouse_id",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                schema: "public",
                table: "lpns");
        }
    }
}
