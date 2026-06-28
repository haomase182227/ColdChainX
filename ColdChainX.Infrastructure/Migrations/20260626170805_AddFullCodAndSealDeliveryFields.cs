using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFullCodAndSealDeliveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "checkin_at",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cod_amount",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "cod_payment_method",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cod_receipt_image_url",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "new_seal_number",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_image_url",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checkin_at",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "cod_amount",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "cod_payment_method",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "cod_receipt_image_url",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "new_seal_number",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "signature_image_url",
                schema: "public",
                table: "lpn_delivery_confirmations");
        }
    }
}
