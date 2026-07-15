using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleInnerDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InnerHeightCm",
                schema: "public",
                table: "vehicles",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InnerLengthCm",
                schema: "public",
                table: "vehicles",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InnerWidthCm",
                schema: "public",
                table: "vehicles",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_warehouse_id",
                schema: "public",
                table: "users",
                column: "warehouse_id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_warehouse",
                schema: "public",
                table: "users",
                column: "warehouse_id",
                principalSchema: "public",
                principalTable: "warehouses",
                principalColumn: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_warehouse",
                schema: "public",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_warehouse_id",
                schema: "public",
                table: "users");

            migrationBuilder.DropColumn(
                name: "InnerHeightCm",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "InnerLengthCm",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "InnerWidthCm",
                schema: "public",
                table: "vehicles");
        }
    }
}
