using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260616000100_AddQuotationAdditionalCharges")]
    public partial class AddQuotationAdditionalCharges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE public.quotations
    ADD COLUMN IF NOT EXISTS additional_charges jsonb;
""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE public.quotations
    DROP COLUMN IF EXISTS additional_charges;
""");
        }
    }
}
