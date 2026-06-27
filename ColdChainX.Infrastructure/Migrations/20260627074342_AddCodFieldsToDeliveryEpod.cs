using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodFieldsToDeliveryEpod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cod_amount",
                schema: "public",
                table: "delivery_epods",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cod_amount_paid",
                schema: "public",
                table: "delivery_epods",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_evidence_image_url",
                schema: "public",
                table: "delivery_epods",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                schema: "public",
                table: "delivery_epods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                schema: "public",
                table: "delivery_epods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cod_amount",
                schema: "public",
                table: "delivery_epods");

            migrationBuilder.DropColumn(
                name: "cod_amount_paid",
                schema: "public",
                table: "delivery_epods");

            migrationBuilder.DropColumn(
                name: "payment_evidence_image_url",
                schema: "public",
                table: "delivery_epods");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "public",
                table: "delivery_epods");

            migrationBuilder.DropColumn(
                name: "payment_status",
                schema: "public",
                table: "delivery_epods");
        }
    }
}
