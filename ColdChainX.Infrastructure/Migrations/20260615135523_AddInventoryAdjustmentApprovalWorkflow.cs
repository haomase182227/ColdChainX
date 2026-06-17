using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAdjustmentApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "movement_id",
                schema: "public",
                table: "inventory_adjustments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                schema: "public",
                table: "inventory_adjustments",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                schema: "public",
                table: "inventory_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pallets_after",
                schema: "public",
                table: "inventory_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "pallets_before",
                schema: "public",
                table: "inventory_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "pallets_changed",
                schema: "public",
                table: "inventory_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "public",
                table: "inventory_adjustments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "public",
                table: "inventory_adjustments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PENDING_APPROVAL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "public",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "public",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "pallets_after",
                schema: "public",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "pallets_before",
                schema: "public",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "pallets_changed",
                schema: "public",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "public",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "public",
                table: "inventory_adjustments");

            migrationBuilder.AlterColumn<Guid>(
                name: "movement_id",
                schema: "public",
                table: "inventory_adjustments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
