using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyWarehouseInventoryTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS public.cycle_count_entries CASCADE;
                DROP TABLE IF EXISTS public.cycle_count_plans CASCADE;
                DROP TABLE IF EXISTS public.inventory_holds CASCADE;
                DROP TABLE IF EXISTS public.inventory_allocations CASCADE;
                DROP TABLE IF EXISTS public.inventory_movements CASCADE;
                DROP TABLE IF EXISTS public.inventory_adjustments CASCADE;
                DROP TABLE IF EXISTS public.inventory_stocks CASCADE;
                DROP TABLE IF EXISTS public.inventory_batches CASCADE;
                DROP TABLE IF EXISTS public.compliance_zoning_rules CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Legacy WMS tables were intentionally removed after the LPN-based flow
            // replaced stock/batch/cycle-count operations.
        }
    }
}
