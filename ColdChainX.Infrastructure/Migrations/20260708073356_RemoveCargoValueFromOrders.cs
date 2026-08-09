using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    public partial class RemoveCargoValueFromOrders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cargo_value",
                schema: "public",
                table: "transport_orders",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}

