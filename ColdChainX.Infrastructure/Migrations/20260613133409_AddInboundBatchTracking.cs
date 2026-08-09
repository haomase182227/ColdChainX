using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    public partial class AddInboundBatchTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "batch_number",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "manufactured_date",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "date",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "batch_number",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "manufactured_date",
                schema: "public",
                table: "warehouse_receipt_items");
        }
    }
}
