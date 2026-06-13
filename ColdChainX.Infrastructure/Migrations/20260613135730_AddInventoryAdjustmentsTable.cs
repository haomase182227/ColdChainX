using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAdjustmentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_adjustments",
                schema: "public",
                columns: table => new
                {
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity_before = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    quantity_changed = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    quantity_after = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    reason_notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_adjustments_pkey", x => x.adjustment_id);
                    table.ForeignKey(
                        name: "fk_adj_movement",
                        column: x => x.movement_id,
                        principalSchema: "public",
                        principalTable: "inventory_movements",
                        principalColumn: "movement_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_adj_stock",
                        column: x => x.stock_id,
                        principalSchema: "public",
                        principalTable: "inventory_stocks",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_movement_id",
                schema: "public",
                table: "inventory_adjustments",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_stock_id",
                schema: "public",
                table: "inventory_adjustments",
                column: "stock_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_adjustments",
                schema: "public");
        }
    }
}
