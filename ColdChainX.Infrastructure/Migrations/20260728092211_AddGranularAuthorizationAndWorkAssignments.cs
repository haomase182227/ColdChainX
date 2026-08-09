using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    public partial class AddGranularAuthorizationAndWorkAssignments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "perm_code",
                schema: "public",
                table: "permissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "public",
                table: "permissions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                schema: "public",
                table: "permissions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "public",
                table: "permissions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                schema: "public",
                table: "permissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "user_permissions",
                schema: "public",
                columns: table => new
                {
                    user_permission_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    valid_to = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("user_permissions_pkey", x => x.user_permission_id);
                    table.ForeignKey(
                        name: "fk_user_permissions_permissions",
                        column: x => x.perm_id,
                        principalSchema: "public",
                        principalTable: "permissions",
                        principalColumn: "perm_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_permissions_users",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_assignments",
                schema: "public",
                columns: table => new
                {
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    task_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    required_permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "NORMAL"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    due_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("work_assignments_pkey", x => x.assignment_id);
                    table.ForeignKey(
                        name: "fk_work_assignments_assigned_to_user",
                        column: x => x.assigned_to_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_assignments_warehouse",
                        column: x => x.warehouse_id,
                        principalSchema: "public",
                        principalTable: "warehouses",
                        principalColumn: "warehouse_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_perm_id",
                schema: "public",
                table: "user_permissions",
                column: "perm_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_permissions_valid_to",
                schema: "public",
                table: "user_permissions",
                column: "valid_to");

            migrationBuilder.CreateIndex(
                name: "user_permissions_user_perm_key",
                schema: "public",
                table: "user_permissions",
                columns: new[] { "user_id", "perm_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_assignments_assignee_status",
                schema: "public",
                table: "work_assignments",
                columns: new[] { "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_assignments_reference",
                schema: "public",
                table: "work_assignments",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_assignments_warehouse_status",
                schema: "public",
                table: "work_assignments",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.Sql(
                """
                INSERT INTO public.permissions
                    (perm_id, perm_code, module, display_name, description, is_active, sort_order)
                VALUES
                    ('a1000000-0000-0000-0000-000000000001', 'AUTHORIZATION.MATRIX.VIEW', 'AUTHORIZATION', 'View permission matrix', 'View role and user permission configuration', true, 10),
                    ('a1000000-0000-0000-0000-000000000002', 'AUTHORIZATION.MATRIX.MANAGE', 'AUTHORIZATION', 'Manage permission matrix', 'Assign permissions to roles and individual users', true, 20),
                    ('a1000000-0000-0000-0000-000000000003', 'WORK_ASSIGNMENT.VIEW_OWN', 'WORK_ASSIGNMENT', 'View assigned work', 'View work assigned to the current user', true, 30),
                    ('a1000000-0000-0000-0000-000000000004', 'WORK_ASSIGNMENT.MANAGE', 'WORK_ASSIGNMENT', 'Manage work assignments', 'Create, view and cancel work assignments', true, 40),
                    ('a1000000-0000-0000-0000-000000000005', 'WORK_ASSIGNMENT.EXECUTE', 'WORK_ASSIGNMENT', 'Execute assigned work', 'Start and complete assigned work', true, 50),
                    ('a1000000-0000-0000-0000-000000000006', 'WAREHOUSE.TASK.VIEW', 'WAREHOUSE', 'View warehouse work', 'View warehouse work and operational context', true, 60),
                    ('a1000000-0000-0000-0000-000000000007', 'WAREHOUSE.ASN.VIEW', 'WAREHOUSE', 'View ASN', 'View advance shipping notices', true, 70),
                    ('a1000000-0000-0000-0000-000000000008', 'WAREHOUSE.RECEIVING.CONFIRM', 'WAREHOUSE', 'Confirm receiving', 'Confirm goods received at a warehouse', true, 80),
                    ('a1000000-0000-0000-0000-000000000009', 'WAREHOUSE.QC.INSPECT', 'WAREHOUSE', 'Perform QC inspection', 'Perform warehouse quality-control inspection', true, 90),
                    ('a1000000-0000-0000-0000-000000000010', 'WAREHOUSE.LOADING.CONFIRM', 'WAREHOUSE', 'Confirm loading', 'Confirm goods loaded onto a vehicle', true, 100),
                    ('a1000000-0000-0000-0000-000000000011', 'WAREHOUSE.INVENTORY.COUNT', 'WAREHOUSE', 'Perform inventory count', 'Perform cycle count and inventory verification', true, 110),
                    ('a1000000-0000-0000-0000-000000000012', 'WAREHOUSE.INVENTORY.ADJUST', 'WAREHOUSE', 'Adjust inventory', 'Create an inventory adjustment', true, 120),
                    ('a1000000-0000-0000-0000-000000000013', 'WAREHOUSE.INVENTORY.APPROVE', 'WAREHOUSE', 'Approve inventory adjustment', 'Approve inventory adjustments and variances', true, 130),
                    ('a1000000-0000-0000-0000-000000000014', 'WAREHOUSE.INCIDENT.RESOLVE', 'WAREHOUSE', 'Resolve warehouse incident', 'Resolve incidents assigned to warehouse operations', true, 140)
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
                WHERE lower(r.role_name) = 'admin'
                ON CONFLICT DO NOTHING;

                INSERT INTO public.role_permissions (role_id, perm_id)
                SELECT r.role_id, p.perm_id
                FROM public.roles r
                JOIN public.permissions p ON p.perm_code IN (
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
                WHERE lower(r.role_name) = 'warehouseworker'
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
                    WHERE perm_code IN (
                        'AUTHORIZATION.MATRIX.VIEW',
                        'AUTHORIZATION.MATRIX.MANAGE',
                        'WORK_ASSIGNMENT.VIEW_OWN',
                        'WORK_ASSIGNMENT.MANAGE',
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
                );
                """);

            migrationBuilder.DropTable(
                name: "user_permissions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "work_assignments",
                schema: "public");

            migrationBuilder.Sql(
                """
                DELETE FROM public.permissions
                WHERE perm_code IN (
                    'AUTHORIZATION.MATRIX.VIEW',
                    'AUTHORIZATION.MATRIX.MANAGE',
                    'WORK_ASSIGNMENT.VIEW_OWN',
                    'WORK_ASSIGNMENT.MANAGE',
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
                );
                """);

            migrationBuilder.DropColumn(
                name: "description",
                schema: "public",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "display_name",
                schema: "public",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "public",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "sort_order",
                schema: "public",
                table: "permissions");

            migrationBuilder.AlterColumn<string>(
                name: "perm_code",
                schema: "public",
                table: "permissions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
