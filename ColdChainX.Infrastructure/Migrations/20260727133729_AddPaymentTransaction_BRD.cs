using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    public partial class AddPaymentTransaction_BRD : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankTransferImageUrl",
                schema: "public",
                table: "claims",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalChargebackOption",
                schema: "public",
                table: "claims",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                schema: "public",
                columns: table => new
                {
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    transaction_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    evidence_image_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("payment_transactions_pkey", x => x.transaction_id);
                    table.ForeignKey(
                        name: "fk_payment_transaction_claim",
                        column: x => x.claim_id,
                        principalSchema: "public",
                        principalTable: "claims",
                        principalColumn: "claim_id");
                    table.ForeignKey(
                        name: "fk_payment_transaction_customer",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "fk_payment_transaction_invoice",
                        column: x => x.invoice_id,
                        principalSchema: "public",
                        principalTable: "invoices",
                        principalColumn: "invoice_id");
                    table.ForeignKey(
                        name: "fk_payment_transaction_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                    table.ForeignKey(
                        name: "fk_payment_transaction_user",
                        column: x => x.created_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_claim_id",
                schema: "public",
                table: "payment_transactions",
                column: "claim_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_created_by",
                schema: "public",
                table: "payment_transactions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_customer_id",
                schema: "public",
                table: "payment_transactions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_invoice_id",
                schema: "public",
                table: "payment_transactions",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_order_id",
                schema: "public",
                table: "payment_transactions",
                column: "order_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_transactions",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "BankTransferImageUrl",
                schema: "public",
                table: "claims");

            migrationBuilder.DropColumn(
                name: "InternalChargebackOption",
                schema: "public",
                table: "claims");
        }
    }
}
