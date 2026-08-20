using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransportOrderRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "receiver_confirmed",
                schema: "public",
                table: "delivery_epods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "receiver_name",
                schema: "public",
                table: "transport_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_phone",
                schema: "public",
                table: "transport_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receiver_confirmed",
                schema: "public",
                table: "delivery_epods");

            migrationBuilder.DropColumn(
                name: "receiver_name",
                schema: "public",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "receiver_phone",
                schema: "public",
                table: "transport_orders");
        }
    }
}
