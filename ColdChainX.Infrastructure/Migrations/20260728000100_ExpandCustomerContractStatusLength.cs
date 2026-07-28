using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260728000100_ExpandCustomerContractStatusLength")]
public partial class ExpandCustomerContractStatusLength : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "status",
            schema: "public",
            table: "customer_contracts",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true,
            defaultValueSql: "'ACTIVE'::character varying",
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true,
            oldDefaultValueSql: "'ACTIVE'::character varying");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "status",
            schema: "public",
            table: "customer_contracts",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true,
            defaultValueSql: "'ACTIVE'::character varying",
            oldClrType: typeof(string),
            oldType: "character varying(30)",
            oldMaxLength: 30,
            oldNullable: true,
            oldDefaultValueSql: "'ACTIVE'::character varying");
    }
}
