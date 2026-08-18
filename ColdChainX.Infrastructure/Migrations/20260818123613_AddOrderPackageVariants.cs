using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPackageVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "order_package_variant_id",
                schema: "public",
                table: "transport_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_package_variants",
                schema: "public",
                columns: table => new
                {
                    order_package_variant_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    packing_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    expected_unit_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    expected_total_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    expected_cbm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_package_variants_pkey", x => x.order_package_variant_id);
                    table.ForeignKey(
                        name: "fk_order_package_variants_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_documents_order_package_variant_id",
                schema: "public",
                table: "transport_documents",
                column: "order_package_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_package_variants_order_id",
                schema: "public",
                table: "order_package_variants",
                column: "order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_td_package_variant",
                schema: "public",
                table: "transport_documents",
                column: "order_package_variant_id",
                principalSchema: "public",
                principalTable: "order_package_variants",
                principalColumn: "order_package_variant_id",
                onDelete: ReferentialAction.Cascade);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_td_package_variant",
                schema: "public",
                table: "transport_documents");

            migrationBuilder.DropTable(
                name: "order_package_variants",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_transport_documents_order_package_variant_id",
                schema: "public",
                table: "transport_documents");

            migrationBuilder.DropColumn(
                name: "order_package_variant_id",
                schema: "public",
                table: "transport_documents");

        }
    }
}
