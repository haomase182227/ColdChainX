using System;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260608000100_AddOrderIdToCustomerContracts")]
    public partial class AddOrderIdToCustomerContracts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "signed_date",
                table: "customer_contracts",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                table: "customer_contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_contracts_order_id",
                table: "customer_contracts",
                column: "order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_cc_orders",
                table: "customer_contracts",
                column: "order_id",
                principalTable: "transport_order",
                principalColumn: "order_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cc_orders",
                table: "customer_contracts");

            migrationBuilder.DropIndex(
                name: "ix_customer_contracts_order_id",
                table: "customer_contracts");

            migrationBuilder.DropColumn(
                name: "order_id",
                table: "customer_contracts");

            migrationBuilder.Sql("UPDATE customer_contracts SET signed_date = CURRENT_DATE WHERE signed_date IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "signed_date",
                table: "customer_contracts",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
