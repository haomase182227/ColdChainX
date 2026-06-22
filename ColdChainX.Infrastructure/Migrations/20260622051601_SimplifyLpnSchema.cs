using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyLpnSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_lpns_receipt_item'
          AND conrelid = 'public.lpns'::regclass
    ) THEN
        ALTER TABLE public.lpns DROP CONSTRAINT fk_lpns_receipt_item;
    END IF;
END $$;
");

            migrationBuilder.DropColumn(
                name: "batch_number",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "discrepancy_pdf_url",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "expected_cbm",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "expected_weight_kg",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "grn_pdf_url",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "height_cm",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "item_code",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "item_name",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "length_cm",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "max_diff_percent",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "picked_at",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "return_pdf_url",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "shipped_at",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "width_cm",
                schema: "public",
                table: "lpns");

            RenameColumnIfNeeded(migrationBuilder, "users", "WarehouseId", "warehouse_id");
            RenameColumnIfNeeded(migrationBuilder, "lpns", "EvidenceImageUrl", "evidence_image_url");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "Phone", "phone");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "WarehouseId", "warehouse_id");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "FileUrl", "file_url");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "CustomerId", "customer_id");

            migrationBuilder.AlterColumn<Guid>(
                name: "receipt_item_id",
                schema: "public",
                table: "lpns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                schema: "public",
                table: "inbound_asn",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "file_url",
                schema: "public",
                table: "inbound_asn",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_lpns_receipt_item'
          AND conrelid = 'public.lpns'::regclass
    ) THEN
        ALTER TABLE public.lpns
            ADD CONSTRAINT fk_lpns_receipt_item
            FOREIGN KEY (receipt_item_id)
            REFERENCES public.warehouse_receipt_items(item_id)
            ON DELETE RESTRICT;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_lpns_receipt_item'
          AND conrelid = 'public.lpns'::regclass
    ) THEN
        ALTER TABLE public.lpns DROP CONSTRAINT fk_lpns_receipt_item;
    END IF;
END $$;
");

            RenameColumnIfNeeded(migrationBuilder, "users", "warehouse_id", "WarehouseId");
            RenameColumnIfNeeded(migrationBuilder, "lpns", "evidence_image_url", "EvidenceImageUrl");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "phone", "Phone");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "warehouse_id", "WarehouseId");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "file_url", "FileUrl");
            RenameColumnIfNeeded(migrationBuilder, "inbound_asn", "customer_id", "CustomerId");

            migrationBuilder.AlterColumn<Guid>(
                name: "receipt_item_id",
                schema: "public",
                table: "lpns",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "batch_number",
                schema: "public",
                table: "lpns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discrepancy_pdf_url",
                schema: "public",
                table: "lpns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_cbm",
                schema: "public",
                table: "lpns",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "expected_weight_kg",
                schema: "public",
                table: "lpns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "grn_pdf_url",
                schema: "public",
                table: "lpns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                schema: "public",
                table: "lpns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "item_code",
                schema: "public",
                table: "lpns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "item_name",
                schema: "public",
                table: "lpns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "length_cm",
                schema: "public",
                table: "lpns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "max_diff_percent",
                schema: "public",
                table: "lpns",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "picked_at",
                schema: "public",
                table: "lpns",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_pdf_url",
                schema: "public",
                table: "lpns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "shipped_at",
                schema: "public",
                table: "lpns",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                schema: "public",
                table: "lpns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                schema: "public",
                table: "inbound_asn",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FileUrl",
                schema: "public",
                table: "inbound_asn",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_lpns_receipt_item'
          AND conrelid = 'public.lpns'::regclass
    ) THEN
        ALTER TABLE public.lpns
            ADD CONSTRAINT fk_lpns_receipt_item
            FOREIGN KEY (receipt_item_id)
            REFERENCES public.warehouse_receipt_items(item_id)
            ON DELETE SET NULL;
    END IF;
END $$;
");
        }

        private static void RenameColumnIfNeeded(MigrationBuilder migrationBuilder, string tableName, string oldName, string newName)
        {
            migrationBuilder.Sql($@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = '{tableName}'
          AND column_name = '{oldName}'
    ) AND NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = '{tableName}'
          AND column_name = '{newName}'
    ) THEN
        ALTER TABLE public.{tableName} RENAME COLUMN ""{oldName}"" TO ""{newName}"";
    END IF;
END $$;
");
        }
    }
}
