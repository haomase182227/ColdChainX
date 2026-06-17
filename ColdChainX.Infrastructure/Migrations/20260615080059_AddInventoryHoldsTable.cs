using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryHoldsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_holds",
                schema: "public",
                columns: table => new
                {
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hold_quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValueSql: "'HOLD'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    release_notes = table.Column<string>(type: "text", nullable: true),
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_holds_pkey", x => x.hold_id);
                    table.ForeignKey(
                        name: "fk_hold_adjustment",
                        column: x => x.adjustment_id,
                        principalSchema: "public",
                        principalTable: "inventory_adjustments",
                        principalColumn: "adjustment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hold_stock",
                        column: x => x.stock_id,
                        principalSchema: "public",
                        principalTable: "inventory_stocks",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hold_user_creator",
                        column: x => x.created_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hold_user_releaser",
                        column: x => x.released_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_adjustment_id",
                schema: "public",
                table: "inventory_holds",
                column: "adjustment_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_created_by",
                schema: "public",
                table: "inventory_holds",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_released_by",
                schema: "public",
                table: "inventory_holds",
                column: "released_by");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_holds_stock_id",
                schema: "public",
                table: "inventory_holds",
                column: "stock_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_holds",
                schema: "public");
        }
    }
}
