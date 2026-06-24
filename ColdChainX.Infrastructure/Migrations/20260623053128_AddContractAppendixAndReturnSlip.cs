using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractAppendixAndReturnSlip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_appendices",
                schema: "public",
                columns: table => new
                {
                    appendix_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appendix_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    adjusted_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    draft_html_content = table.Column<string>(type: "text", nullable: true),
                    pdf_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("contract_appendices_pkey", x => x.appendix_id);
                    table.ForeignKey(
                        name: "fk_contract_appendices_contract",
                        column: x => x.contract_id,
                        principalSchema: "public",
                        principalTable: "customer_contracts",
                        principalColumn: "contract_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_contract_appendices_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inbound_return_slips",
                schema: "public",
                columns: table => new
                {
                    return_slip_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lpn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slip_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    returned_weight_kg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    returned_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    returned_qty = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    pdf_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inbound_return_slips_pkey", x => x.return_slip_id);
                    table.ForeignKey(
                        name: "fk_inbound_return_slips_lpn",
                        column: x => x.lpn_id,
                        principalSchema: "public",
                        principalTable: "lpns",
                        principalColumn: "lpn_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inbound_return_slips_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contract_appendices_contract_id",
                schema: "public",
                table: "contract_appendices",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "IX_contract_appendices_order_id",
                schema: "public",
                table: "contract_appendices",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "uq_contract_appendices_number",
                schema: "public",
                table: "contract_appendices",
                column: "appendix_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_return_slips_lpn_id",
                schema: "public",
                table: "inbound_return_slips",
                column: "lpn_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_return_slips_order_id",
                schema: "public",
                table: "inbound_return_slips",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "uq_inbound_return_slips_code",
                schema: "public",
                table: "inbound_return_slips",
                column: "slip_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_appendices",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inbound_return_slips",
                schema: "public");
        }
    }
}
