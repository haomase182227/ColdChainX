using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncEntityMappingsAfterAzureFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE public.quotations
    ADD COLUMN IF NOT EXISTS additional_charges jsonb;
""");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "public",
                table: "customer_contracts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValueSql: "'DRAFT'::character varying",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldDefaultValueSql: "'ACTIVE'::character varying");

            migrationBuilder.Sql("""
CREATE INDEX IF NOT EXISTS "IX_drivers_user_id"
    ON public.drivers(user_id);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE constraint_schema = 'public'
          AND table_name = 'drivers'
          AND constraint_name = 'fk_drivers_users'
    ) THEN
        ALTER TABLE public.drivers
            ADD CONSTRAINT fk_drivers_users
            FOREIGN KEY (user_id)
            REFERENCES public.users(user_id);
    END IF;
END $$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE public.drivers DROP CONSTRAINT IF EXISTS fk_drivers_users;
DROP INDEX IF EXISTS public."IX_drivers_user_id";
""");

            migrationBuilder.Sql("""
ALTER TABLE public.quotations
    DROP COLUMN IF EXISTS additional_charges;
""");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "public",
                table: "customer_contracts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValueSql: "'ACTIVE'::character varying",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldDefaultValueSql: "'DRAFT'::character varying");
        }
    }
}
