using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseColumnsAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "public",
                table: "warehouses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_max_temp",
                schema: "public",
                table: "warehouses",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_min_temp",
                schema: "public",
                table: "warehouses",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                schema: "public",
                table: "warehouses",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by",
                schema: "public",
                table: "warehouses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                schema: "public",
                table: "warehouses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                schema: "public",
                table: "warehouses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "warehouse_code",
                schema: "public",
                table: "warehouses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "warehouse_type",
                schema: "public",
                table: "warehouses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Populate unique codes and default types for existing rows before applying the unique index
            migrationBuilder.Sql("UPDATE public.warehouses SET warehouse_code = 'WH-' || SUBSTR(warehouse_id::text, 1, 8) WHERE warehouse_code = '' OR warehouse_code IS NULL;");
            migrationBuilder.Sql("UPDATE public.warehouses SET warehouse_type = 'COLD' WHERE warehouse_type = '' OR warehouse_type IS NULL;");

            migrationBuilder.CreateIndex(
                name: "warehouses_warehouse_code_key",
                schema: "public",
                table: "warehouses",
                column: "warehouse_code",
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            // Commented out as they already exist in the target database from ERD.txt initialization
            /*
            migrationBuilder.CreateIndex(
                name: "IX_drivers_user_id",
                schema: "public",
                table: "drivers",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_drivers_users",
                schema: "public",
                table: "drivers",
                column: "user_id",
                principalSchema: "public",
                principalTable: "users",
                principalColumn: "user_id");
            */
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
            migrationBuilder.DropForeignKey(
                name: "fk_drivers_users",
                schema: "public",
                table: "drivers");
            */

            migrationBuilder.DropIndex(
                name: "warehouses_warehouse_code_key",
                schema: "public",
                table: "warehouses");

            /*
            migrationBuilder.DropIndex(
                name: "IX_drivers_user_id",
                schema: "public",
                table: "drivers");
            */

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "default_max_temp",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "default_min_temp",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "warehouse_code",
                schema: "public",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "warehouse_type",
                schema: "public",
                table: "warehouses");
        }
    }
}
