using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileVehicleInnerDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns already exist in some deployed databases from an older model.
            // IF NOT EXISTS keeps their current values while making fresh databases consistent.
            migrationBuilder.Sql("""
                ALTER TABLE public.vehicles
                    ADD COLUMN IF NOT EXISTS "InnerHeightCm" numeric(10,2) NULL,
                    ADD COLUMN IF NOT EXISTS "InnerLengthCm" numeric(10,2) NULL,
                    ADD COLUMN IF NOT EXISTS "InnerWidthCm" numeric(10,2) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally keep the columns: this migration may only have adopted
            // columns that predated the current EF migration history.
        }
    }
}
