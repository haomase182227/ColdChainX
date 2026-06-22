using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWarehouseReceiptItemAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lpns_receipt_item",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropTable(
                name: "attachment_audit_history",
                schema: "public");

            migrationBuilder.DropTable(
                name: "warehouse_evidence_attachments",
                schema: "public");

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_id",
                schema: "public",
                table: "lpns",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE public.lpns
                SET receipt_id = wri.receipt_id
                FROM public.warehouse_receipt_items wri
                WHERE public.lpns.receipt_item_id = wri.item_id;
            ");

            migrationBuilder.Sql("DELETE FROM public.lpns WHERE receipt_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "receipt_id",
                schema: "public",
                table: "lpns",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "receipt_item_id",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropTable(
                name: "warehouse_receipt_items",
                schema: "public");

            migrationBuilder.CreateIndex(
                name: "IX_lpns_receipt_id",
                schema: "public",
                table: "lpns",
                column: "receipt_id");

            migrationBuilder.AlterColumn<string>(
                name: "evidence_image_url",
                schema: "public",
                table: "lpns",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_lpns_warehouse_receipts_receipt_id",
                schema: "public",
                table: "lpns",
                column: "receipt_id",
                principalSchema: "public",
                principalTable: "warehouse_receipts",
                principalColumn: "receipt_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lpns_warehouse_receipts_receipt_id",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropIndex(
                name: "IX_lpns_receipt_id",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "receipt_id",
                schema: "public",
                table: "lpns");

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_item_id",
                schema: "public",
                table: "lpns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_lpns_receipt_item_id",
                schema: "public",
                table: "lpns",
                column: "receipt_item_id");

            migrationBuilder.AlterColumn<string>(
                name: "evidence_image_url",
                schema: "public",
                table: "lpns",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "warehouse_receipt_items",
                schema: "public",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_qty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    condition_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValueSql: "'GOOD'::character varying"),
                    country_of_origin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expected_qty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    item_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    manufactured_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    product_category = table.Column<int>(type: "integer", nullable: false),
                    qr_code = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouse_receipt_items_pkey", x => x.item_id);
                    table.ForeignKey(
                        name: "fk_wri_wr",
                        column: x => x.receipt_id,
                        principalSchema: "public",
                        principalTable: "warehouse_receipts",
                        principalColumn: "receipt_id");
                });

            migrationBuilder.CreateTable(
                name: "warehouse_evidence_attachments",
                schema: "public",
                columns: table => new
                {
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_receipt_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    captured_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    category = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    file_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    format = table.Column<int>(type: "integer", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    issuer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    seal_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sub_category = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouse_evidence_attachments_pkey", x => x.attachment_id);
                    table.CheckConstraint("chk_attachment_target", "(warehouse_receipt_id IS NOT NULL)::int + (warehouse_receipt_item_id IS NOT NULL)::int + (outbound_order_id IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "fk_att_outbound",
                        column: x => x.outbound_order_id,
                        principalSchema: "public",
                        principalTable: "outbound_orders",
                        principalColumn: "outbound_order_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_att_receipt",
                        column: x => x.warehouse_receipt_id,
                        principalSchema: "public",
                        principalTable: "warehouse_receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_att_receipt_item",
                        column: x => x.warehouse_receipt_item_id,
                        principalSchema: "public",
                        principalTable: "warehouse_receipt_items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "attachment_audit_history",
                schema: "public",
                columns: table => new
                {
                    history_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    new_status = table.Column<int>(type: "integer", nullable: false),
                    previous_status = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("attachment_audit_history_pkey", x => x.history_id);
                    table.ForeignKey(
                        name: "fk_history_attachment",
                        column: x => x.attachment_id,
                        principalSchema: "public",
                        principalTable: "warehouse_evidence_attachments",
                        principalColumn: "attachment_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_history_attachment",
                schema: "public",
                table: "attachment_audit_history",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "idx_att_outbound",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "outbound_order_id");

            migrationBuilder.CreateIndex(
                name: "idx_att_receipt",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "warehouse_receipt_id");

            migrationBuilder.CreateIndex(
                name: "idx_att_receipt_item",
                schema: "public",
                table: "warehouse_evidence_attachments",
                column: "warehouse_receipt_item_id");

            migrationBuilder.CreateIndex(
                name: "idx_warehouse_receipt_items_barcode",
                schema: "public",
                table: "warehouse_receipt_items",
                column: "barcode");

            migrationBuilder.CreateIndex(
                name: "idx_warehouse_receipt_items_item_code",
                schema: "public",
                table: "warehouse_receipt_items",
                column: "item_code");

            migrationBuilder.CreateIndex(
                name: "idx_warehouse_receipt_items_receipt_id",
                schema: "public",
                table: "warehouse_receipt_items",
                column: "receipt_id");

            migrationBuilder.AddForeignKey(
                name: "fk_lpns_receipt_item",
                schema: "public",
                table: "lpns",
                column: "receipt_item_id",
                principalSchema: "public",
                principalTable: "warehouse_receipt_items",
                principalColumn: "item_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
