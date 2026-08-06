using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260806000000_ConsolidateLegacyWarehouseRolesIntoWarehouseWorker")]
public partial class ConsolidateLegacyWarehouseRolesIntoWarehouseWorker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                warehouse_worker_role_id uuid;
                legacy_warehouse_role_id uuid;
            BEGIN
                SELECT role_id INTO warehouse_worker_role_id
                FROM public.roles
                WHERE lower(role_name) = 'warehouseworker'
                LIMIT 1;

                IF warehouse_worker_role_id IS NULL THEN
                    SELECT role_id INTO legacy_warehouse_role_id
                    FROM public.roles
                    WHERE lower(role_name) = 'warehouseoperator'
                    LIMIT 1;

                    IF legacy_warehouse_role_id IS NOT NULL THEN
                        UPDATE public.roles
                        SET role_name = 'WarehouseWorker',
                            description = 'Warehouse worker'
                        WHERE role_id = legacy_warehouse_role_id;

                        warehouse_worker_role_id := legacy_warehouse_role_id;
                    ELSE
                        INSERT INTO public.roles (role_name, description)
                        VALUES ('WarehouseWorker', 'Warehouse worker')
                        RETURNING role_id INTO warehouse_worker_role_id;
                    END IF;
                END IF;

                UPDATE public.users
                SET role_id = warehouse_worker_role_id
                WHERE role_id IN (
                    SELECT role_id
                    FROM public.roles
                    WHERE lower(role_name) IN ('warehouseoperator', 'loader', 'manager')
                      AND role_id <> warehouse_worker_role_id
                );

                INSERT INTO public.role_permissions (role_id, perm_id)
                SELECT warehouse_worker_role_id, rp.perm_id
                FROM public.role_permissions rp
                JOIN public.roles r ON r.role_id = rp.role_id
                WHERE lower(r.role_name) IN ('warehouseoperator', 'loader', 'manager')
                  AND r.role_id <> warehouse_worker_role_id
                ON CONFLICT DO NOTHING;

                DELETE FROM public.role_permissions
                WHERE role_id IN (
                    SELECT role_id
                    FROM public.roles
                    WHERE lower(role_name) IN ('warehouseoperator', 'loader', 'manager')
                      AND role_id <> warehouse_worker_role_id
                );

                DELETE FROM public.roles
                WHERE lower(role_name) IN ('warehouseoperator', 'loader', 'manager')
                  AND role_id <> warehouse_worker_role_id;

                UPDATE public.roles
                SET role_name = 'WarehouseWorker',
                    description = 'Warehouse worker'
                WHERE role_id = warehouse_worker_role_id;

                INSERT INTO public.role_permissions (role_id, perm_id)
                SELECT warehouse_worker_role_id, p.perm_id
                FROM public.permissions p
                WHERE p.perm_code IN (
                    'WORK_ASSIGNMENT.VIEW_OWN',
                    'WORK_ASSIGNMENT.EXECUTE',
                    'WAREHOUSE.TASK.VIEW',
                    'WAREHOUSE.ASN.VIEW',
                    'WAREHOUSE.RECEIVING.CONFIRM',
                    'WAREHOUSE.QC.INSPECT',
                    'WAREHOUSE.LOADING.CONFIRM',
                    'WAREHOUSE.INVENTORY.COUNT',
                    'WAREHOUSE.INVENTORY.ADJUST',
                    'WAREHOUSE.INVENTORY.APPROVE',
                    'WAREHOUSE.INCIDENT.RESOLVE'
                )
                ON CONFLICT DO NOTHING;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE public.roles
            SET role_name = 'WarehouseOperator',
                description = 'Warehouse operator'
            WHERE lower(role_name) = 'warehouseworker';
            """);
    }
}
