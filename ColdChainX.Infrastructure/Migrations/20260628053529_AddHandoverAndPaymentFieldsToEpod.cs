using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverAndPaymentFieldsToEpod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "handover_confirmed_at",
                schema: "public",
                table: "delivery_epods",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handover_pdf_url",
                schema: "public",
                table: "delivery_epods",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_confirmed_at",
                schema: "public",
                table: "delivery_epods",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "handover_confirmed_at",
                schema: "public",
                table: "delivery_epods");

            migrationBuilder.DropColumn(
                name: "handover_pdf_url",
                schema: "public",
                table: "delivery_epods");

            migrationBuilder.DropColumn(
                name: "payment_confirmed_at",
                schema: "public",
                table: "delivery_epods");
        }
    }
}
