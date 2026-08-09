using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    public partial class HardenPermissionConcurrency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS ux_work_assignments_active_work
                ON public.work_assignments (task_type, reference_type, reference_id, assigned_to_user_id)
                WHERE status IN ('ASSIGNED', 'IN_PROGRESS');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS public.ux_work_assignments_active_work;");
        }
    }
}
