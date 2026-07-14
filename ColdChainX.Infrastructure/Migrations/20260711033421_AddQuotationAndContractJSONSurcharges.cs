using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationAndContractJSONSurcharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MandatoryCharges",
                schema: "public",
                table: "quotations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionalServicesMenu",
                schema: "public",
                table: "quotations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalContractTotalAmount",
                schema: "public",
                table: "customer_contracts",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedOptionalServices",
                schema: "public",
                table: "customer_contracts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MandatoryCharges",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "OptionalServicesMenu",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "FinalContractTotalAmount",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "SelectedOptionalServices",
                schema: "public",
                table: "customer_contracts");
        }
    }
}
