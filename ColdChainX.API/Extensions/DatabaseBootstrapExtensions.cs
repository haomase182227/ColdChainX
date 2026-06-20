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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database migrations skipped or failed, but continuing with patches and seeding.");
            }

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

                var sqlCheckConstraints = @"DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='inventory_stocks') THEN
        IF NOT EXISTS (
            SELECT 1 
            FROM pg_constraint 
            WHERE conrelid = 'public.inventory_stocks'::regclass AND conname = 'ck_inventory_stocks_quantity_on_hand_gte_zero'
        ) THEN
            ALTER TABLE public.inventory_stocks ADD CONSTRAINT ck_inventory_stocks_quantity_on_hand_gte_zero CHECK (quantity_on_hand >= 0);
        END IF;

        IF NOT EXISTS (
            SELECT 1 
            FROM pg_constraint 
            WHERE conrelid = 'public.inventory_stocks'::regclass AND conname = 'ck_inventory_stocks_quantity_allocated_gte_zero'
        ) THEN
            ALTER TABLE public.inventory_stocks ADD CONSTRAINT ck_inventory_stocks_quantity_allocated_gte_zero CHECK (quantity_allocated >= 0);
        END IF;
    END IF;
END $$;";

                logger.LogInformation("Ensuring inventory_stocks check constraints exist...");
                await db.Database.ExecuteSqlRawAsync(sqlCheckConstraints);
                logger.LogInformation("Inventory check constraints check executed.");

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
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply database compatibility patch or schema bootstrap.");
            }

            var sqlSeedDemoData = @"DO $$
DECLARE
    loc_wh_id uuid := '77b07384-d113-46c6-950c-619f7e5b32cd';
    cust_id uuid := 'c7b07384-d113-46c6-950c-619f7e5b32cd';
    wh_id uuid := '87b07384-d113-46c6-950c-619f7e5b32cd';
    admin_id uuid := '11111111-1111-1111-1111-111111111111';
    v_zone_id uuid;
    v_loc_stage_id uuid;
    batch_1_id uuid := '91b07384-d113-46c6-950c-619f7e5b32cd';
    batch_2_id uuid := '92b07384-d113-46c6-950c-619f7e5b32cd';
    batch_3_id uuid := '93b07384-d113-46c6-950c-619f7e5b32cd';
BEGIN
    -- Clear existing demo warehouse receipts/items to prevent foreign key issues
    DELETE FROM public.warehouse_receipt_items WHERE receipt_id IN (
        'e1b07384-d113-46c6-950c-619f7e5b32cd', 'e2b07384-d113-46c6-950c-619f7e5b32cd', 'e3b07384-d113-46c6-950c-619f7e5b32cf',
        'e1b07384-d113-46c6-950c-619f7e5b3001', 'e1b07384-d113-46c6-950c-619f7e5b3002', 'e1b07384-d113-46c6-950c-619f7e5b3003',
        'e1b07384-d113-46c6-950c-619f7e5b3004', 'e1b07384-d113-46c6-950c-619f7e5b3005', 'e1b07384-d113-46c6-950c-619f7e5b3006',
        'e1b07384-d113-46c6-950c-619f7e5b3007', 'e1b07384-d113-46c6-950c-619f7e5b3008', 'e1b07384-d113-46c6-950c-619f7e5b3009',
        'e1b07384-d113-46c6-950c-619f7e5b3010'
    );
    DELETE FROM public.warehouse_receipt_items WHERE receipt_id IN (
        SELECT receipt_id FROM public.warehouse_receipts WHERE order_id IN (
            'd3b07384-d113-46c6-950c-619f7e5b32cd', 'd3b07384-d113-46c6-950c-619f7e5b32ce', 'd3b07384-d113-46c6-950c-619f7e5b32cf',
            'd3b07384-d113-46c6-950c-619f7e5b3001', 'd3b07384-d113-46c6-950c-619f7e5b3002', 'd3b07384-d113-46c6-950c-619f7e5b3003',
            'd3b07384-d113-46c6-950c-619f7e5b3004', 'd3b07384-d113-46c6-950c-619f7e5b3005', 'd3b07384-d113-46c6-950c-619f7e5b3006',
            'd3b07384-d113-46c6-950c-619f7e5b3007', 'd3b07384-d113-46c6-950c-619f7e5b3008', 'd3b07384-d113-46c6-950c-619f7e5b3009',
            'd3b07384-d113-46c6-950c-619f7e5b3010'
        )
    );
    DELETE FROM public.warehouse_receipts WHERE order_id IN (
        'd3b07384-d113-46c6-950c-619f7e5b32cd', 'd3b07384-d113-46c6-950c-619f7e5b32ce', 'd3b07384-d113-46c6-950c-619f7e5b32cf',
        'd3b07384-d113-46c6-950c-619f7e5b3001', 'd3b07384-d113-46c6-950c-619f7e5b3002', 'd3b07384-d113-46c6-950c-619f7e5b3003',
        'd3b07384-d113-46c6-950c-619f7e5b3004', 'd3b07384-d113-46c6-950c-619f7e5b3005', 'd3b07384-d113-46c6-950c-619f7e5b3006',
        'd3b07384-d113-46c6-950c-619f7e5b3007', 'd3b07384-d113-46c6-950c-619f7e5b3008', 'd3b07384-d113-46c6-950c-619f7e5b3009',
        'd3b07384-d113-46c6-950c-619f7e5b3010'
    );
    DELETE FROM public.quotations WHERE order_id IN (
        'd3b07384-d113-46c6-950c-619f7e5b32cd', 'd3b07384-d113-46c6-950c-619f7e5b32ce', 'd3b07384-d113-46c6-950c-619f7e5b32cf',
        'd3b07384-d113-46c6-950c-619f7e5b3001', 'd3b07384-d113-46c6-950c-619f7e5b3002', 'd3b07384-d113-46c6-950c-619f7e5b3003',
        'd3b07384-d113-46c6-950c-619f7e5b3004', 'd3b07384-d113-46c6-950c-619f7e5b3005', 'd3b07384-d113-46c6-950c-619f7e5b3006',
        'd3b07384-d113-46c6-950c-619f7e5b3007', 'd3b07384-d113-46c6-950c-619f7e5b3008', 'd3b07384-d113-46c6-950c-619f7e5b3009',
        'd3b07384-d113-46c6-950c-619f7e5b3010'
    );
    DELETE FROM public.inventory_stocks WHERE stock_id IN (
        'f1b07384-d113-46c6-950c-619f7e5b3001', 'f1b07384-d113-46c6-950c-619f7e5b3002', 'f1b07384-d113-46c6-950c-619f7e5b3003',
        'f1b07384-d113-46c6-950c-619f7e5b3004', 'f1b07384-d113-46c6-950c-619f7e5b3005', 'f1b07384-d113-46c6-950c-619f7e5b3006',
        'f1b07384-d113-46c6-950c-619f7e5b3007', 'f1b07384-d113-46c6-950c-619f7e5b3008', 'f1b07384-d113-46c6-950c-619f7e5b3009',
        'f1b07384-d113-46c6-950c-619f7e5b3010'
    );
    DELETE FROM public.transport_orders WHERE order_id IN (
        'd3b07384-d113-46c6-950c-619f7e5b3001', 'd3b07384-d113-46c6-950c-619f7e5b3002', 'd3b07384-d113-46c6-950c-619f7e5b3003',
        'd3b07384-d113-46c6-950c-619f7e5b3004', 'd3b07384-d113-46c6-950c-619f7e5b3005', 'd3b07384-d113-46c6-950c-619f7e5b3006',
        'd3b07384-d113-46c6-950c-619f7e5b3007', 'd3b07384-d113-46c6-950c-619f7e5b3008', 'd3b07384-d113-46c6-950c-619f7e5b3009',
        'd3b07384-d113-46c6-950c-619f7e5b3010'
    );

    -- 1. Seed Customer
    IF NOT EXISTS (SELECT 1 FROM public.customers WHERE customer_id = cust_id) THEN
        INSERT INTO public.customers (customer_id, company_name, tax_code, email, status, payment_term, created_at)
        VALUES (cust_id, 'Vinamilk Corporation', '0313456789', 'info@vinamilk.com', 'ACTIVE', 30, CURRENT_TIMESTAMP);
    END IF;

    -- 2. Seed Locations (Destination and Warehouse Location)
    IF NOT EXISTS (SELECT 1 FROM public.locations WHERE location_id = '17b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.locations (location_id, customer_id, address, latitude, longitude, status, created_at)
        VALUES ('17b07384-d113-46c6-950c-619f7e5b32cd', cust_id, '10 Mai Chi Tho, District 2, Ho Chi Minh City, Vietnam', 10.776, 106.700, 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.locations WHERE location_id = loc_wh_id) THEN
        INSERT INTO public.locations (location_id, address, latitude, longitude, status, created_at)
        VALUES (loc_wh_id, 'Thu Duc, Ho Chi Minh', 10.8231, 106.6297, 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;

    -- 3. Seed Warehouse
    IF NOT EXISTS (SELECT 1 FROM public.warehouses WHERE warehouse_id = wh_id) THEN
        INSERT INTO public.warehouses (warehouse_id, warehouse_name, address, max_pallets, current_pallets, status, created_at, warehouse_code, warehouse_type)
        VALUES (wh_id, 'Hub HCM - Thu Duc', 'Thu Duc, Ho Chi Minh', 100, 0, 'ACTIVE', CURRENT_TIMESTAMP, 'WH-HCM-TD', 'COLD');
    END IF;

    -- Seed Zones & Locations for Warehouse
    SELECT zone_id INTO v_zone_id FROM public.warehouse_zones WHERE warehouse_id = wh_id AND zone_code = 'RECEIVING' LIMIT 1;
    IF v_zone_id IS NULL THEN
        v_zone_id := gen_random_uuid();
        INSERT INTO public.warehouse_zones (zone_id, warehouse_id, zone_code, zone_name, zone_type, storage_type, max_capacity_pallets, current_pallets, status, created_at)
        VALUES (v_zone_id, wh_id, 'RECEIVING', 'Receiving Stage Zone', 'RECEIVING', 'FLOOR', 1000, 0, 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;

    SELECT location_id INTO v_loc_stage_id FROM public.warehouse_locations WHERE zone_id = v_zone_id AND location_code = 'RCV-STAGE-01' LIMIT 1;
    IF v_loc_stage_id IS NULL THEN
        v_loc_stage_id := gen_random_uuid();
        INSERT INTO public.warehouse_locations (location_id, zone_id, location_code, max_capacity_pallets, current_pallets, status, description, created_at)
        VALUES (v_loc_stage_id, v_zone_id, 'RCV-STAGE-01', 1000, 0, 'ACTIVE', 'Default Inbound Receiving Stage Location', CURRENT_TIMESTAMP);
    END IF;

    -- 4. Seed TransportOrders with pickup_location and IN_WAREHOUSE status
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b32cd', 'TRK-DEMO-001', cust_id, 'Sữa chua Vinamilk', 'Dairy', '4', 100.00, 100.00, 1.50, 1.50, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 5000000.00, 'IN_WAREHOUSE', 10, 'Thung', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32ce') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b32ce', 'TRK-DEMO-ODOR', cust_id, 'Sầu riêng Cái Mơn', 'Durian', '15', 200.00, 200.00, 2.50, 2.50, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 8000000.00, 'IN_WAREHOUSE', 20, 'Thung', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cf') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b32cf', 'TRK-DEMO-QCFAIL', cust_id, 'Thịt bò Mỹ nhập khẩu', 'Frozen Meat', '-18', 150.00, 150.00, 1.00, 1.00, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 15000000.00, 'IN_WAREHOUSE', 15, 'Thung', CURRENT_TIMESTAMP);
    END IF;

    -- Update existing ones if they already exist but have null pickup_location or wrong status
    UPDATE public.transport_orders
    SET pickup_location = loc_wh_id, status = 'IN_WAREHOUSE'
    WHERE order_id IN ('d3b07384-d113-46c6-950c-619f7e5b32cd', 'd3b07384-d113-46c6-950c-619f7e5b32ce', 'd3b07384-d113-46c6-950c-619f7e5b32cf');

    -- Seed WarehouseReceipts
    IF NOT EXISTS (SELECT 1 FROM public.warehouse_receipts WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b32cd', 'REC-DEMO-001', 'd3b07384-d113-46c6-950c-619f7e5b32cd', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 10, 10, CURRENT_TIMESTAMP);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.warehouse_receipts WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32ce') THEN
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e2b07384-d113-46c6-950c-619f7e5b32cd', 'REC-DEMO-002', 'd3b07384-d113-46c6-950c-619f7e5b32ce', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 20, 20, CURRENT_TIMESTAMP);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.warehouse_receipts WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cf') THEN
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e3b07384-d113-46c6-950c-619f7e5b32cf', 'REC-DEMO-003', 'd3b07384-d113-46c6-950c-619f7e5b32cf', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 15, 15, CURRENT_TIMESTAMP);
    END IF;

    -- Seed WarehouseReceiptItems
    IF NOT EXISTS (SELECT 1 FROM public.warehouse_receipt_items WHERE receipt_id = 'e1b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b32ce', 'e1b07384-d113-46c6-950c-619f7e5b32cd', 'Sữa chua Vinamilk', 'VINAMILK-YOGURT', 0, 'Vietnam', 'Thung', 10, 10, 'GOOD', 'B001', '2026-06-01', '2026-12-31', 100.00);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.warehouse_receipt_items WHERE receipt_id = 'e2b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e2b07384-d113-46c6-950c-619f7e5b32ce', 'e2b07384-d113-46c6-950c-619f7e5b32cd', 'Sầu riêng Cái Mơn', 'DURIAN-CAIMON', 0, 'Vietnam', 'Thung', 20, 20, 'GOOD', 'B002', '2026-06-01', '2026-12-31', 200.00);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.warehouse_receipt_items WHERE receipt_id = 'e3b07384-d113-46c6-950c-619f7e5b32cf') THEN
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e3b07384-d113-46c6-950c-619f7e5b32ce', 'e3b07384-d113-46c6-950c-619f7e5b32cf', 'Thịt bò Mỹ nhập khẩu', 'BEEF-US', 0, 'USA', 'Thung', 15, 15, 'GOOD', 'B003', '2026-06-01', '2026-12-31', 150.00);
    END IF;

    -- Seed Batches
    IF NOT EXISTS (SELECT 1 FROM public.inventory_batches WHERE batch_id = batch_1_id) THEN
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES (batch_1_id, 'VINAMILK-YOGURT', 'B001', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.inventory_batches WHERE batch_id = batch_2_id) THEN
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES (batch_2_id, 'DURIAN-CAIMON', 'B002', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.inventory_batches WHERE batch_id = batch_3_id) THEN
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES (batch_3_id, 'BEEF-US', 'B003', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
    END IF;

    -- Seed InventoryStocks
    IF NOT EXISTS (SELECT 1 FROM public.inventory_stocks WHERE stock_id = 'f1b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b32cd', v_loc_stage_id, cust_id, 'VINAMILK-YOGURT', 'Sữa chua Vinamilk', 'Thung', batch_1_id, 10, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 2, 8);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.inventory_stocks WHERE stock_id = 'f2b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f2b07384-d113-46c6-950c-619f7e5b32cd', v_loc_stage_id, cust_id, 'DURIAN-CAIMON', 'Sầu riêng Cái Mơn', 'Thung', batch_2_id, 20, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 10, 20);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.inventory_stocks WHERE stock_id = 'f3b07384-d113-46c6-950c-619f7e5b32cd') THEN
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f3b07384-d113-46c6-950c-619f7e5b32cd', v_loc_stage_id, cust_id, 'BEEF-US', 'Thịt bò Mỹ nhập khẩu', 'Thung', batch_3_id, 15, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, -25, -15);
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

    END IF;

    -- =========================================================================
    -- Seed 10 new test orders in warehouse
    -- =========================================================================
    
    -- TRK-TEST-001 (Kem bơ Đà Lạt)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3001') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3001', 'TRK-TEST-001', cust_id, 'Kem bơ Đà Lạt', 'Ice Cream', '-18', 120.00, 120.00, 1.20, 1.20, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 6000000.00, 'IN_WAREHOUSE', 12, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3001', 'REC-TEST-001', 'd3b07384-d113-46c6-950c-619f7e5b3001', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 12, 12, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4001', 'e1b07384-d113-46c6-950c-619f7e5b3001', 'Kem bơ Đà Lạt', 'AVOCADO-ICECREAM', 0, 'Vietnam', 'Thung', 12, 12, 'GOOD', 'BT001', '2026-06-01', '2026-12-31', 120.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3001', 'AVOCADO-ICECREAM', 'BT001', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3001', v_loc_stage_id, cust_id, 'AVOCADO-ICECREAM', 'Kem bơ Đà Lạt', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3001', 12, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, -25, -15);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3001', 'd3b07384-d113-46c6-950c-619f7e5b3001', 180000.00, 0.00, 0.00, 14400.00, 194400.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-002 (Sữa tươi TH True Milk)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3002') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3002', 'TRK-TEST-002', cust_id, 'Sữa tươi TH True Milk', 'Dairy', '4', 150.00, 150.00, 1.50, 1.50, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 3000000.00, 'IN_WAREHOUSE', 15, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3002', 'REC-TEST-002', 'd3b07384-d113-46c6-950c-619f7e5b3002', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 15, 15, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4002', 'e1b07384-d113-46c6-950c-619f7e5b3002', 'Sữa tươi TH True Milk', 'TH-MILK', 0, 'Vietnam', 'Thung', 15, 15, 'GOOD', 'BT002', '2026-06-01', '2026-12-31', 150.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3002', 'TH-MILK', 'BT002', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3002', v_loc_stage_id, cust_id, 'TH-MILK', 'Sữa tươi TH True Milk', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3002', 15, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 2, 8);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3002', 'd3b07384-d113-46c6-950c-619f7e5b3002', 225000.00, 0.00, 0.00, 18000.00, 243000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-003 (Tôm sú đông lạnh)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3003') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3003', 'TRK-TEST-003', cust_id, 'Tôm sú đông lạnh', 'Seafood', '-18', 250.00, 250.00, 2.50, 2.50, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 12000000.00, 'IN_WAREHOUSE', 25, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3003', 'REC-TEST-003', 'd3b07384-d113-46c6-950c-619f7e5b3003', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 25, 25, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4003', 'e1b07384-d113-46c6-950c-619f7e5b3003', 'Tôm sú đông lạnh', 'FROZEN-SHRIMP', 0, 'Vietnam', 'Thung', 25, 25, 'GOOD', 'BT003', '2026-06-01', '2026-12-31', 250.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3003', 'FROZEN-SHRIMP', 'BT003', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3003', v_loc_stage_id, cust_id, 'FROZEN-SHRIMP', 'Tôm sú đông lạnh', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3003', 25, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, -25, -15);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3003', 'd3b07384-d113-46c6-950c-619f7e5b3003', 375000.00, 0.00, 0.00, 30000.00, 405000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-004 (Hành tây Đà Lạt)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3004') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3004', 'TRK-TEST-004', cust_id, 'Hành tây Đà Lạt', 'Vegetables', '10', 500.00, 500.00, 5.00, 5.00, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 4000000.00, 'IN_WAREHOUSE', 50, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3004', 'REC-TEST-004', 'd3b07384-d113-46c6-950c-619f7e5b3004', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 50, 50, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4004', 'e1b07384-d113-46c6-950c-619f7e5b3004', 'Hành tây Đà Lạt', 'DALAT-ONION', 0, 'Vietnam', 'Thung', 50, 50, 'GOOD', 'BT004', '2026-06-01', '2026-12-31', 500.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3004', 'DALAT-ONION', 'BT004', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3004', v_loc_stage_id, cust_id, 'DALAT-ONION', 'Hành tây Đà Lạt', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3004', 50, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 8, 15);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3004', 'd3b07384-d113-46c6-950c-619f7e5b3004', 750000.00, 0.00, 0.00, 60000.00, 810000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-005 (Vắc xin phòng cúm)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3005') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3005', 'TRK-TEST-005', cust_id, 'Vắc xin phòng cúm', 'Pharma', '4', 5.00, 5.00, 0.10, 0.10, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 50000000.00, 'IN_WAREHOUSE', 1, 'Hop', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3005', 'REC-TEST-005', 'd3b07384-d113-46c6-950c-619f7e5b3005', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 1, 1, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4005', 'e1b07384-d113-46c6-950c-619f7e5b3005', 'Vắc xin phòng cúm', 'INFLUENZA-VACCINE', 0, 'France', 'Hop', 1, 1, 'GOOD', 'BT005', '2026-06-01', '2026-12-31', 5.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3005', 'INFLUENZA-VACCINE', 'BT005', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3005', v_loc_stage_id, cust_id, 'INFLUENZA-VACCINE', 'Vắc xin phòng cúm', 'Hop', '91b07384-d113-46c6-950c-619f7e5b3005', 1, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 2, 8);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3005', 'd3b07384-d113-46c6-950c-619f7e5b3005', 50000.00, 0.00, 0.00, 4000.00, 54000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-006 (Thịt heo sạch G-Kitchen)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3006') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3006', 'TRK-TEST-006', cust_id, 'Thịt heo sạch G-Kitchen', 'Fresh Meat', '2', 300.00, 300.00, 3.00, 3.00, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 15000000.00, 'IN_WAREHOUSE', 30, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3006', 'REC-TEST-006', 'd3b07384-d113-46c6-950c-619f7e5b3006', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 30, 30, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4006', 'e1b07384-d113-46c6-950c-619f7e5b3006', 'Thịt heo sạch G-Kitchen', 'GKITCHEN-PORK', 0, 'Vietnam', 'Thung', 30, 30, 'GOOD', 'BT006', '2026-06-01', '2026-12-31', 300.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3006', 'GKITCHEN-PORK', 'BT006', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3006', v_loc_stage_id, cust_id, 'GKITCHEN-PORK', 'Thịt heo sạch G-Kitchen', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3006', 30, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 0, 4);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3006', 'd3b07384-d113-46c6-950c-619f7e5b3006', 450000.00, 0.00, 0.00, 36000.00, 486000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-007 (Sữa chua uống Probi)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3007') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3007', 'TRK-TEST-007', cust_id, 'Sữa chua uống Probi', 'Dairy', '4', 80.00, 80.00, 0.80, 0.80, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 2500000.00, 'IN_WAREHOUSE', 8, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3007', 'REC-TEST-007', 'd3b07384-d113-46c6-950c-619f7e5b3007', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 8, 8, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4007', 'e1b07384-d113-46c6-950c-619f7e5b3007', 'Sữa chua uống Probi', 'PROBI-DRINK', 0, 'Vietnam', 'Thung', 8, 8, 'GOOD', 'BT007', '2026-06-01', '2026-12-31', 80.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3007', 'PROBI-DRINK', 'BT007', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3007', v_loc_stage_id, cust_id, 'PROBI-DRINK', 'Sữa chua uống Probi', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3007', 8, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 2, 8);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3007', 'd3b07384-d113-46c6-950c-619f7e5b3007', 120000.00, 0.00, 0.00, 9600.00, 129600.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-008 (Cá hồi Nauy tươi)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3008') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3008', 'TRK-TEST-008', cust_id, 'Cá hồi Nauy tươi', 'Seafood', '0', 100.00, 100.00, 1.00, 1.00, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 20000000.00, 'IN_WAREHOUSE', 10, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3008', 'REC-TEST-008', 'd3b07384-d113-46c6-950c-619f7e5b3008', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 10, 10, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4008', 'e1b07384-d113-46c6-950c-619f7e5b3008', 'Cá hồi Nauy tươi', 'NORWAY-SALMON', 0, 'Norway', 'Thung', 10, 10, 'GOOD', 'BT008', '2026-06-01', '2026-12-31', 100.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3008', 'NORWAY-SALMON', 'BT008', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3008', v_loc_stage_id, cust_id, 'NORWAY-SALMON', 'Cá hồi Nauy tươi', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3008', 10, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, -2, 2);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3008', 'd3b07384-d113-46c6-950c-619f7e5b3008', 150000.00, 0.00, 0.00, 12000.00, 162000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-009 (Kem hộp Merino)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3009') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3009', 'TRK-TEST-009', cust_id, 'Kem hộp Merino', 'Ice Cream', '-18', 90.00, 90.00, 0.90, 0.90, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 4500000.00, 'IN_WAREHOUSE', 9, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3009', 'REC-TEST-009', 'd3b07384-d113-46c6-950c-619f7e5b3009', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 9, 9, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4009', 'e1b07384-d113-46c6-950c-619f7e5b3009', 'Kem hộp Merino', 'MERINO-ICECREAM', 0, 'Vietnam', 'Thung', 9, 9, 'GOOD', 'BT009', '2026-06-01', '2026-12-31', 90.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3009', 'MERINO-ICECREAM', 'BT009', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3009', v_loc_stage_id, cust_id, 'MERINO-ICECREAM', 'Kem hộp Merino', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3009', 9, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, -25, -15);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3009', 'd3b07384-d113-46c6-950c-619f7e5b3009', 135000.00, 0.00, 0.00, 10800.00, 145800.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    -- TRK-TEST-010 (Phô mai Con Bò Cười)
    IF NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b3010') THEN
        INSERT INTO public.transport_orders (order_id, tracking_code, customer_id, item_name, category, temp_condition, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, pickup_location, dest_location, cargo_value, status, quantity, packing_type, created_at)
        VALUES ('d3b07384-d113-46c6-950c-619f7e5b3010', 'TRK-TEST-010', cust_id, 'Phô mai Con Bò Cười', 'Cheese', '10', 60.00, 60.00, 0.60, 0.60, loc_wh_id, '17b07384-d113-46c6-950c-619f7e5b32cd', 3500000.00, 'IN_WAREHOUSE', 6, 'Thung', CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipts (receipt_id, receipt_code, order_id, warehouse_id, receipt_type, deliverer_name, receiver_id, reference_doc_no, total_expected_qty, total_actual_qty, created_at)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b3010', 'REC-TEST-010', 'd3b07384-d113-46c6-950c-619f7e5b3010', wh_id, 'INBOUND', 'Driver Demo', admin_id, 'COMPLETED', 6, 6, CURRENT_TIMESTAMP);
        
        INSERT INTO public.warehouse_receipt_items (item_id, receipt_id, item_name, item_code, product_category, country_of_origin, unit, expected_qty, actual_qty, condition_status, batch_number, manufactured_date, expiry_date, actual_weight_kg)
        VALUES ('e1b07384-d113-46c6-950c-619f7e5b4010', 'e1b07384-d113-46c6-950c-619f7e5b3010', 'Phô mai Con Bò Cười', 'CHEESE-LAUGHING', 0, 'Vietnam', 'Thung', 6, 6, 'GOOD', 'BT010', '2026-06-01', '2026-12-31', 60.00);
        
        INSERT INTO public.inventory_batches (batch_id, item_code, batch_number, manufactured_date, expiry_date, status, created_at)
        VALUES ('91b07384-d113-46c6-950c-619f7e5b3010', 'CHEESE-LAUGHING', 'BT010', '2026-06-01', '2026-12-31', 'ACTIVE', CURRENT_TIMESTAMP);
        
        INSERT INTO public.inventory_stocks (stock_id, location_id, customer_id, item_code, item_name, unit, batch_id, quantity_on_hand, quantity_allocated, inbound_date, status, created_at, pallet_count, required_temp_min, required_temp_max)
        VALUES ('f1b07384-d113-46c6-950c-619f7e5b3010', v_loc_stage_id, cust_id, 'CHEESE-LAUGHING', 'Phô mai Con Bò Cười', 'Thung', '91b07384-d113-46c6-950c-619f7e5b3010', 6, 0, CURRENT_TIMESTAMP, 'AVAILABLE', CURRENT_TIMESTAMP, 1, 8, 15);
        
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b3010', 'd3b07384-d113-46c6-950c-619f7e5b3010', 90000.00, 0.00, 0.00, 7200.00, 97200.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.quotations WHERE order_id = 'd3b07384-d113-46c6-950c-619f7e5b32cf') THEN
        INSERT INTO public.quotations (quote_id, order_id, base_freight, last_mile_surcharge, vas_amount, vat_amount, final_amount, status, created_at)
        VALUES ('b7b07384-d113-46c6-950c-619f7e5b32cf', 'd3b07384-d113-46c6-950c-619f7e5b32cf', 225000.00, 0.00, 0.00, 18000.00, 243000.00, 'APPROVED', CURRENT_TIMESTAMP);
    END IF;
END $$;";

            try
            {
                // Seed Users using EF Core
                var adminExists = await db.Users.AnyAsync(u => u.UserId == Guid.Parse("11111111-1111-1111-1111-111111111111"));
                if (!adminExists)
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

            try
            {
                logger.LogInformation("Seeding demo data for warehouse receipt testing...");
                await db.Database.ExecuteSqlRawAsync(sqlSeedDemoData);
                logger.LogInformation("Demo data seeded.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database compatibility patch or demo data seeding skipped/failed.");
            }
        }
    }
}
