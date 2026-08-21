using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260821000200_AddOrderUpdateOwnPermission")]
public partial class AddOrderUpdateOwnPermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO public.permissions
                (perm_id, perm_code, module, display_name, description, is_active, sort_order)
            VALUES
                ('a1000000-0000-0000-0000-000000000016',
                 'ORDER.UPDATE_OWN',
                 'ORDER',
                 'Update own orders',
                 'Update an order owned by the current customer',
                 true,
                 160)
            ON CONFLICT (perm_code) DO UPDATE SET
                module = EXCLUDED.module,
                display_name = EXCLUDED.display_name,
                description = EXCLUDED.description,
                is_active = EXCLUDED.is_active,
                sort_order = EXCLUDED.sort_order;

            INSERT INTO public.role_permissions (role_id, perm_id)
            SELECT role.role_id, permission.perm_id
            FROM public.roles role
            CROSS JOIN public.permissions permission
            WHERE lower(role.role_name) = 'customer'
              AND permission.perm_code = 'ORDER.UPDATE_OWN'
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
                WHERE perm_code = 'ORDER.UPDATE_OWN'
            );

            DELETE FROM public.permissions
            WHERE perm_code = 'ORDER.UPDATE_OWN';
            """);
    }
}
