using System;
using System.Threading.Tasks;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ColdChainX.API.Extensions
{
    public static class DatabaseBootstrapExtensions
    {
        public static async Task ApplyAuthSchemaCompatibilityPatchAsync(this IServiceProvider services, ILogger logger)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var sqlRename = @"DO $$
DECLARE
    colname text;
    legacy_names text[] := array['id','Id','ID','role','Role','phone_number','PhoneNumber','fullname','FullName','passwordhash','PasswordHash','createdat','CreatedAt','updatedat','UpdatedAt','refreshtoken','RefreshToken','refreshtokenexpirytime','RefreshTokenExpiryTime'];
    target_map jsonb := '{{""id"": ""user_id"", ""role"": ""role_id"", ""phone_number"": ""phone"", ""fullname"": ""full_name"", ""passwordhash"": ""password_hash"", ""createdat"": ""created_at"", ""updatedat"": ""updated_at"", ""refreshtoken"": ""refresh_token"", ""refreshtokenexpirytime"": ""refresh_token_expiry_time""}}'::jsonb;
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'users') THEN
        FOREACH colname IN ARRAY legacy_names
        LOOP
            IF EXISTS (
                SELECT 1
                FROM information_schema.columns c
                WHERE c.table_schema='public' AND c.table_name='users' AND lower(c.column_name)=lower(colname)
                AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns c2 WHERE c2.table_schema='public' AND c2.table_name='users' AND c2.column_name = target_map->>lower(colname)
                )
            ) THEN
                SELECT column_name INTO colname FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)=lower(colname) LIMIT 1;
                EXECUTE format('ALTER TABLE public.users RENAME COLUMN %I TO %I', colname, target_map->>lower(colname));
            END IF;
        END LOOP;

        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='username') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN username character varying(50)';
            EXECUTE 'UPDATE public.users SET username = email WHERE username IS NULL';
        END IF;

        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='status') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN status character varying(20) DEFAULT ''ACTIVE''';
        END IF;

        -- Ensure refresh token columns exist (idempotent)
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='refresh_token') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN refresh_token text';
        END IF;

        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='refresh_token_expiry_time') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN refresh_token_expiry_time timestamp without time zone';
        END IF;

        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND column_name='status' AND data_type='integer') THEN
            EXECUTE 'ALTER TABLE public.users ALTER COLUMN status TYPE character varying(20) USING (CASE status WHEN 0 THEN ''ACTIVE'' WHEN 1 THEN ''INACTIVE'' ELSE status::text END)';
            EXECUTE 'ALTER TABLE public.users ALTER COLUMN status SET DEFAULT ''ACTIVE''';
        END IF;
    END IF;
END $$;";

                logger.LogInformation("Applying DB rename/compatibility SQL...");
                await db.Database.ExecuteSqlRawAsync(sqlRename);
                logger.LogInformation("DB rename/compatibility SQL executed.");
                // Ensure common user columns exist (idempotent)
                var sqlEnsureUserCols = @"DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='users') THEN
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='created_at') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='updated_at') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN updated_at timestamp without time zone';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='password_hash') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN password_hash character varying(255)';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='full_name') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN full_name character varying(100)';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='phone') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN phone character varying(20)';
        END IF;
    END IF;
END $$;";

                logger.LogInformation("Ensuring common user columns exist...");
                await db.Database.ExecuteSqlRawAsync(sqlEnsureUserCols);
                logger.LogInformation("User columns ensured.");

                var sqlRoles = @"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'roles'
    ) THEN
        CREATE TABLE public.roles (
            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            role_name character varying(50) NOT NULL,
            description text NULL,
            created_at timestamp without time zone NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE UNIQUE INDEX IF NOT EXISTS roles_role_name_key ON public.roles(role_name);
    END IF;
END $$;";

                logger.LogInformation("Ensuring roles table exists...");
                await db.Database.ExecuteSqlRawAsync(sqlRoles);
                logger.LogInformation("Roles table check executed.");

                var sqlSeed = @"INSERT INTO public.roles (role_name, description)
SELECT v.role_name, v.description
FROM (
    VALUES
        ('Admin', 'System administrator'),
        ('Manager', 'Operations manager'),
        ('Customer', 'Customer account'),
        ('Driver', 'Driver account'),
        ('Dispatcher', 'Container dispatcher'),
        ('Sale', 'Take care customer')
) AS v(role_name, description)
WHERE NOT EXISTS (
    SELECT 1
    FROM public.roles r
    WHERE lower(r.role_name) = lower(v.role_name)
);";

                logger.LogInformation("Seeding roles (if missing)...");
                await db.Database.ExecuteSqlRawAsync(sqlSeed);
                logger.LogInformation("Roles seeding executed.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database compatibility patch skipped or failed.");
            }
        }
    }
}
