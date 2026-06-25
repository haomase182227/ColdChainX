using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDimensionsToOrderAndLpn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "length_cm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                schema: "public",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                schema: "public",
                table: "lpns",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "length_cm",
                schema: "public",
                table: "lpns",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                schema: "public",
                table: "lpns",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "height_cm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "length_cm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "width_cm",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "height_cm",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "length_cm",
                schema: "public",
                table: "lpns");

            migrationBuilder.DropColumn(
                name: "width_cm",
                schema: "public",
                table: "lpns");
        }
    }
}
