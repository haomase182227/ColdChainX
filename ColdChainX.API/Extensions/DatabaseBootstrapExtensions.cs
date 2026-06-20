using System;
using System.Threading.Tasks;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Core.Entities;
using Microsoft.AspNetCore.Identity;
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
                logger.LogInformation("Applying database migrations...");
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied.");

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
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='created_by') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN created_by uuid';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='updated_by') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN updated_by uuid';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='deleted_by') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN deleted_by uuid';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND lower(column_name)='deleted_at') THEN
            EXECUTE 'ALTER TABLE public.users ADD COLUMN deleted_at timestamp without time zone';
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
        ('Customer', 'Customer account'),
        ('Driver', 'Driver account'),
        ('Dispatcher', 'Container dispatcher'),
        ('Sales', 'Sales staff')
) AS v(role_name, description)
WHERE NOT EXISTS (
    SELECT 1
    FROM public.roles r
    WHERE lower(r.role_name) = lower(v.role_name)
);";

                logger.LogInformation("Seeding roles (if missing)...");
                await db.Database.ExecuteSqlRawAsync(sqlSeed);
                logger.LogInformation("Roles seeding executed.");

                var sqlEnsureTransportOrder = @"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'transport_orders'
    ) THEN
        CREATE TABLE public.transport_orders (
            order_id uuid NOT NULL DEFAULT gen_random_uuid(),
            tracking_code character varying(50) NOT NULL,
            customer_id uuid NULL,
            item_name character varying(150) NOT NULL,
            category character varying(50) NOT NULL,
            temp_condition character varying(20) NOT NULL,
            expected_weight_kg numeric(10,2) NOT NULL,
            actual_weight_kg numeric(10,2) NOT NULL,
            expected_cbm numeric(8,2) NOT NULL,
            actual_cbm numeric(8,2) NULL,
            pickup_location uuid NULL,
            dest_location uuid NULL,
            cargo_value numeric(15,2) NOT NULL,
            status character varying(30) NOT NULL,
            master_trip_id uuid NULL,
            quantity integer NOT NULL DEFAULT 1,
            packing_type character varying(50) NOT NULL DEFAULT 'Thung',
            created_at timestamp without time zone NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT transport_orders_pkey PRIMARY KEY (order_id)
        );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'transport_orders' AND column_name = 'quantity'
    ) THEN
        ALTER TABLE public.transport_orders ADD COLUMN quantity integer NOT NULL DEFAULT 1;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'transport_orders' AND column_name = 'packing_type'
    ) THEN
        ALTER TABLE public.transport_orders ADD COLUMN packing_type character varying(50) NOT NULL DEFAULT 'Thung';
    END IF;

    CREATE INDEX IF NOT EXISTS ""IX_transport_orders_customer_id""
        ON public.transport_orders(customer_id);

    CREATE UNIQUE INDEX IF NOT EXISTS transport_orders_tracking_code_key
        ON public.transport_orders(tracking_code);
END $$;";

                logger.LogInformation("Ensuring transport_orders table exists...");
                await db.Database.ExecuteSqlRawAsync(sqlEnsureTransportOrder);
                logger.LogInformation("transport_orders table ensured.");

                var sqlSeedDemoData = @"DO $$
BEGIN
    -- 1. Seed Customer
    IF NOT EXISTS (SELECT 1 FROM public.customers WHERE customer_id = 'c7b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.customers (customer_id, company_name, tax_code, email, status, payment_term, created_at)
        VALUES ('c7b07384-d113-46c6-950c-619f7e5b32cd', 'Vinamilk Corporation', '0313456789', 'info@vinamilk.com', 'ACTIVE', 30, CURRENT_TIMESTAMP);
    END IF;

    -- 2. Seed Location
    IF NOT EXISTS (SELECT 1 FROM public.locations WHERE location_id = '17b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.locations (location_id, customer_id, address, latitude, longitude, status, created_at)
        VALUES ('17b07384-d113-46c6-950c-619f7e5b32cd', 'c7b07384-d113-46c6-950c-619f7e5b32cd', '10 Mai Chi Tho, District 2, Ho Chi Minh City, Vietnam', 10.776, 106.700, 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;

    -- 3. Seed Warehouse
    IF NOT EXISTS (SELECT 1 FROM public.warehouses WHERE warehouse_id = '87b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.warehouses (warehouse_id, warehouse_name, address, max_pallets, current_pallets, status, created_at)
        VALUES ('87b07384-d113-46c6-950c-619f7e5b32cd', 'Hub HCM - Thu Duc', 'Thu Duc, Ho Chi Minh', 100, 0, 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;

    -- 4. Seed TransportOrder
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b32cd', 'TRK-DEMO-001', 'c7b07384-d113-46c6-950c-619f7e5b32cd', 'Sữa chua Vinamilk', 'Dairy', '4', 100.00, 0.00, 1.50, NULL, NULL, '17b07384-d113-46c6-950c-619f7e5b32cd', 5000000.00, 'ASSIGNED', 10, 'Thung', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32ce') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b32ce', 'TRK-DEMO-ODOR', 'c7b07384-d113-46c6-950c-619f7e5b32cd', 'Sầu riêng Cái Mơn', 'Durian', '15', 200.00, 0.00, 2.50, NULL, NULL, '17b07384-d113-46c6-950c-619f7e5b32cd', 8000000.00, 'ASSIGNED', 20, 'Thung', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cf') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b32cf', 'TRK-DEMO-QCFAIL', 'c7b07384-d113-46c6-950c-619f7e5b32cd', 'Thịt bò Mỹ nhập khẩu', 'Frozen Meat', '-18', 150.00, 0.00, 1.00, NULL, NULL, '17b07384-d113-46c6-950c-619f7e5b32cd', 15000000.00, 'ASSIGNED', 15, 'Thung', CURRENT_TIMESTAMP);
    END IF;

    -- 5. Seed Pricing Matrices
    IF NOT EXISTS (SELECT 1 FROM public.pricing_matrix WHERE origin_city = 'Ho Chi Minh' AND dest_city = 'Ho Chi Minh' AND pricing_unit = 'KG') THEN
        INSERT INTO public.pricing_matrix (price_id, origin_city, dest_city, pricing_unit, unit_price, effective_date)
        VALUES ('a1b07384-d113-46c6-950c-619f7e5b32cd', 'Ho Chi Minh', 'Ho Chi Minh', 'KG', 1500.00, '2026-01-01');
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.pricing_matrix WHERE origin_city = 'Ho Chi Minh' AND dest_city = 'Ho Chi Minh' AND pricing_unit = 'CBM') THEN
        INSERT INTO public.pricing_matrix (price_id, origin_city, dest_city, pricing_unit, unit_price, effective_date)
        VALUES ('a2b07384-d113-46c6-950c-619f7e5b32cd', 'Ho Chi Minh', 'Ho Chi Minh', 'CBM', 120000.00, '2026-01-01');
    END IF;

    -- 6. Seed Quotation
    IF NOT EXISTS (SELECT 1 FROM public.quotations WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b32cd', 'd3b07384-d113-46c6-950c-619f7e5b32cd', 150000.00, 0.00, 0.00, 12000.00, 162000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.quotations WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32ce') THEN
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b32ce', 'd3b07384-d113-46c6-950c-619f7e5b32ce', 300000.00, 0.00, 0.00, 24000.00, 324000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.quotations WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cf') THEN
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b32cf', 'd3b07384-d113-46c6-950c-619f7e5b32cf', 225000.00, 0.00, 0.00, 18000.00, 243000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;
END $$;";

                logger.LogInformation("Seeding demo data for warehouse receipt testing...");
                await db.Database.ExecuteSqlRawAsync(sqlSeedDemoData);
                logger.LogInformation("Demo data seeded.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database compatibility patch or demo data seeding skipped/failed.");
            }

            try
            {
                // Seed Users using EF Core
                var hasAnyUser = await db.Users.AnyAsync();
                if (!hasAnyUser)
                {
                    logger.LogInformation("Seeding default users...");
                    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
                    
                    var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
                    var managerRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager");
                    var customerRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Customer");
                    var driverRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Driver");

                    if (adminRole != null)
                    {
                        var admin = new User
                        {
                            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            Username = "admin01",
                            Email = "admin01@coldchainx.com",
                            FullName = "System Admin",
                            RoleId = adminRole.RoleId,
                            Status = "ACTIVE",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        };
                        admin.PasswordHash = passwordHasher.HashPassword(admin, "Password@123");
                        db.Users.Add(admin);
                    }

                    if (managerRole != null)
                    {
                        var manager = new User
                        {
                            UserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            Username = "manager01",
                            Email = "manager01@coldchainx.com",
                            FullName = "Warehouse Manager",
                            RoleId = managerRole.RoleId,
                            Status = "ACTIVE",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        };
                        manager.PasswordHash = passwordHasher.HashPassword(manager, "Password@123");
                        db.Users.Add(manager);
                    }

                    if (customerRole != null)
                    {
                        var customer = new User
                        {
                            UserId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                            Username = "customer01",
                            Email = "customer01@coldchainx.com",
                            FullName = "Vinamilk Customer",
                            RoleId = customerRole.RoleId,
                            Status = "ACTIVE",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        };
                        customer.PasswordHash = passwordHasher.HashPassword(customer, "Password@123");
                        db.Users.Add(customer);
                    }

                    if (driverRole != null)
                    {
                        var driver = new User
                        {
                            UserId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                            Username = "driver01",
                            Email = "driver01@coldchainx.com",
                            FullName = "Main Driver",
                            RoleId = driverRole.RoleId,
                            Status = "ACTIVE",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        };
                        driver.PasswordHash = passwordHasher.HashPassword(driver, "Password@123");
                        db.Users.Add(driver);
                    }

                    await db.SaveChangesAsync();
                    logger.LogInformation("Default users seeded.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed default users.");
            }
        }
    }
}
