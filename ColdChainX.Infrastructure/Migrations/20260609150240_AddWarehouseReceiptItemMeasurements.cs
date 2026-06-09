using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseReceiptItemMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "actual_weight_kg",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "barcode",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "length_cm",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qr_code",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                schema: "public",
                table: "warehouse_receipt_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                schema: "public",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "refresh_token_expiry_time",
                schema: "public",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "refresh_token",
                schema: "public",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "public",
                table: "users",
                type: "uuid",
                maxLength: 36,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "public",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                schema: "public",
                table: "users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by",
                schema: "public",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                schema: "public",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "packing_type",
                schema: "public",
                table: "transport_order",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValueSql: "'Thùng'::character varying");

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                schema: "public",
                table: "transport_order",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "file_url",
                schema: "public",
                table: "quotations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "signed_date",
                schema: "public",
                table: "customer_contracts",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                schema: "public",
                table: "customer_contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_contracts_order_id",
                schema: "public",
                table: "customer_contracts",
                column: "order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_cc_orders",
                schema: "public",
                table: "customer_contracts",
                column: "order_id",
                principalSchema: "public",
                principalTable: "transport_order",
                principalColumn: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cc_orders",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropIndex(
                name: "IX_customer_contracts_order_id",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "actual_weight_kg",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "barcode",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "height_cm",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "length_cm",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "qr_code",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "width_cm",
                schema: "public",
                table: "warehouse_receipt_items");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "public",
                table: "users");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "public",
                table: "users");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "public",
                table: "users");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "public",
                table: "users");

            migrationBuilder.DropColumn(
                name: "packing_type",
                schema: "public",
                table: "transport_order");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "public",
                table: "transport_order");

            migrationBuilder.DropColumn(
                name: "file_url",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "order_id",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                schema: "public",
                table: "users",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "refresh_token_expiry_time",
                schema: "public",
                table: "users",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "refresh_token",
                schema: "public",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "public",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldMaxLength: 36);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "signed_date",
                schema: "public",
                table: "customer_contracts",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
