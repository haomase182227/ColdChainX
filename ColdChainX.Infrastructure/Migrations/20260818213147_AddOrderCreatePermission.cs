using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260818213147_AddOrderCreatePermission")]
public partial class AddOrderCreatePermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO public.permissions
                (perm_id, perm_code, module, display_name, description, is_active, sort_order)
            VALUES
                ('a1000000-0000-0000-0000-000000000015',
                 'ORDER.CREATE',
                 'ORDER',
                 'Create order',
                 'Create an order for the current customer or on behalf of a customer',
                 true,
                 150)
            ON CONFLICT (perm_code) DO UPDATE SET
                module = EXCLUDED.module,
                display_name = EXCLUDED.display_name,
                description = EXCLUDED.description,
                is_active = EXCLUDED.is_active,
                sort_order = EXCLUDED.sort_order;

            INSERT INTO public.role_permissions (role_id, perm_id)
            SELECT r.role_id, p.perm_id
            FROM public.roles r
            CROSS JOIN public.permissions p
            WHERE lower(r.role_name) IN ('admin', 'customer', 'sales')
              AND p.perm_code = 'ORDER.CREATE'
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM public.role_permissions
            WHERE perm_id IN (
                SELECT perm_id
                FROM public.permissions
                WHERE perm_code = 'ORDER.CREATE'
            );

            DELETE FROM public.permissions
            WHERE perm_code = 'ORDER.CREATE';
            """);
    }
}
