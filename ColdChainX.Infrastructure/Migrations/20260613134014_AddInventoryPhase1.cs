using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_batches",
                schema: "public",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    manufactured_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_batches_pkey", x => x.batch_id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "public",
                columns: table => new
                {
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_movements_pkey", x => x.movement_id);
                    table.ForeignKey(
                        name: "fk_movement_batch",
                        column: x => x.batch_id,
                        principalSchema: "public",
                        principalTable: "inventory_batches",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movement_from_loc",
                        column: x => x.from_location_id,
                        principalSchema: "public",
                        principalTable: "warehouse_locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movement_to_loc",
                        column: x => x.to_location_id,
                        principalSchema: "public",
                        principalTable: "warehouse_locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_stocks",
                schema: "public",
                columns: table => new
                {
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "0.00"),
                    quantity_allocated = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "0.00"),
                    inbound_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'AVAILABLE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_stocks_pkey", x => x.stock_id);
                    table.ForeignKey(
                        name: "fk_stock_batch",
                        column: x => x.batch_id,
                        principalSchema: "public",
                        principalTable: "inventory_batches",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_location",
                        column: x => x.location_id,
                        principalSchema: "public",
                        principalTable: "warehouse_locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_item_batch",
                schema: "public",
                table: "inventory_batches",
                columns: new[] { "item_code", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_batch_id",
                schema: "public",
                table: "inventory_movements",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_from_location_id",
                schema: "public",
                table: "inventory_movements",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_to_location_id",
                schema: "public",
                table: "inventory_movements",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_stocks_batch_id",
                schema: "public",
                table: "inventory_stocks",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "uq_location_item_batch",
                schema: "public",
                table: "inventory_stocks",
                columns: new[] { "location_id", "item_code", "batch_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_stocks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inventory_batches",
                schema: "public");
        }
    }
}
