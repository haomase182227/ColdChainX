using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLpnPackageVariantLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lpn_package_variant_lines",
                schema: "public",
                columns: table => new
                {
                    lpn_package_variant_line_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    lpn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_package_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    packing_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    expected_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    expected_cbm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    actual_cbm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    recorded_temperature = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    diff_percent = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    has_discrepancy = table.Column<bool>(type: "boolean", nullable: false),
                    evidence_image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("lpn_package_variant_lines_pkey", x => x.lpn_package_variant_line_id);
                    table.ForeignKey(
                        name: "fk_lpn_package_variant_lines_lpn",
                        column: x => x.lpn_id,
                        principalSchema: "public",
                        principalTable: "lpns",
                        principalColumn: "lpn_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lpn_package_variant_lines_variant",
                        column: x => x.order_package_variant_id,
                        principalSchema: "public",
                        principalTable: "order_package_variants",
                        principalColumn: "order_package_variant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_lpn_package_variant_lines_lpn_id",
                schema: "public",
                table: "lpn_package_variant_lines",
                column: "lpn_id");

            migrationBuilder.CreateIndex(
                name: "idx_lpn_package_variant_lines_variant_id",
                schema: "public",
                table: "lpn_package_variant_lines",
                column: "order_package_variant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lpn_package_variant_lines",
                schema: "public");

        }
    }
}
