using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPutawayTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pallet_count",
                schema: "public",
                table: "inventory_stocks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "required_temp_max",
                schema: "public",
                table: "inventory_stocks",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "required_temp_min",
                schema: "public",
                table: "inventory_stocks",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pallet_count",
                schema: "public",
                table: "inventory_stocks");

            migrationBuilder.DropColumn(
                name: "required_temp_max",
                schema: "public",
                table: "inventory_stocks");

            migrationBuilder.DropColumn(
                name: "required_temp_min",
                schema: "public",
                table: "inventory_stocks");
        }
    }
}
