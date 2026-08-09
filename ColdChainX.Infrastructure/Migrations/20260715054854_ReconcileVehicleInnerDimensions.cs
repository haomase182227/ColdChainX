using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    public partial class ReconcileVehicleInnerDimensions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.vehicles
                    ADD COLUMN IF NOT EXISTS "InnerHeightCm" numeric(10,2) NULL,
                    ADD COLUMN IF NOT EXISTS "InnerLengthCm" numeric(10,2) NULL,
                    ADD COLUMN IF NOT EXISTS "InnerWidthCm" numeric(10,2) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
