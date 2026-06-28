using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedMoreInboundInStockLpns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    seed_customer_id uuid := '71000000-0000-0000-0000-000000000001';
                    pickup_location_id uuid := '72000000-0000-0000-0000-000000000001';
                    dest_location_1_id uuid := '72000000-0000-0000-0000-000000000101';
                    dest_location_2_id uuid := '72000000-0000-0000-0000-000000000102';
                    dest_location_3_id uuid := '72000000-0000-0000-0000-000000000103';
                    seed_warehouse_id uuid := '73000000-0000-0000-0000-000000000001';
                    receiver_id uuid := '74000000-0000-0000-0000-000000000001';

                    order_1_id uuid := '75000000-0000-0000-0000-000000000201';
                    order_2_id uuid := '75000000-0000-0000-0000-000000000202';
                    order_3_id uuid := '75000000-0000-0000-0000-000000000203';

                    receipt_1_id uuid := '76000000-0000-0000-0000-000000000201';
                    receipt_2_id uuid := '76000000-0000-0000-0000-000000000202';
                    receipt_3_id uuid := '76000000-0000-0000-0000-000000000203';
                BEGIN
                    INSERT INTO public.customers
                        (customer_id, company_name, tax_code, address, email, payment_term, status, created_at)
                    SELECT
                        seed_customer_id,
                        'Seed LTL Cold Chain Customer',
                        'SEED-LTL-2026',
                        'Khu cong nghiep Tan Tao, Binh Tan, TP.HCM',
                        'seed-ltl@coldchainx.local',
                        30,
                        'ACTIVE',
                        TIMESTAMP '2026-06-25 09:00:00'
                    WHERE NOT EXISTS (
                        SELECT 1 FROM public.customers
                        WHERE customer_id = seed_customer_id OR tax_code = 'SEED-LTL-2026'
                    );

                    INSERT INTO public.locations
                        (location_id, customer_id, address, latitude, longitude, status, created_at)
                    SELECT pickup_location_id, seed_customer_id, 'ColdChainX Hub Tan Tao - Seed Pickup', 10.7425210, 106.6128730, 'ACTIVE', TIMESTAMP '2026-06-25 09:00:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.locations WHERE location_id = pickup_location_id);

                    INSERT INTO public.locations
                        (location_id, customer_id, address, latitude, longitude, status, created_at)
                    SELECT dest_location_1_id, seed_customer_id, 'Phong kham Seed Pharma Quan 1', 10.7768890, 106.7008060, 'ACTIVE', TIMESTAMP '2026-06-25 09:00:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.locations WHERE location_id = dest_location_1_id);

                    INSERT INTO public.locations
                        (location_id, customer_id, address, latitude, longitude, status, created_at)
                    SELECT dest_location_2_id, seed_customer_id, 'Nha hang Seed Seafood Quan 7', 10.7297860, 106.7218480, 'ACTIVE', TIMESTAMP '2026-06-25 09:00:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.locations WHERE location_id = dest_location_2_id);

                    INSERT INTO public.locations
                        (location_id, customer_id, address, latitude, longitude, status, created_at)
                    SELECT dest_location_3_id, seed_customer_id, 'Sieu thi Seed Fresh Thu Duc', 10.8494090, 106.7537050, 'ACTIVE', TIMESTAMP '2026-06-25 09:00:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.locations WHERE location_id = dest_location_3_id);

                    INSERT INTO public.warehouses
                        (warehouse_id, warehouse_name, address, warehouse_code, warehouse_type, default_min_temp, default_max_temp, max_pallets, current_pallets, status, created_at)
                    SELECT
                        seed_warehouse_id,
                        'Seed Cold Storage Hub Tan Tao',
                        'ColdChainX Hub Tan Tao - Seed Pickup',
                        'WH-SEED-LTL-01',
                        'COLD_STORAGE',
                        -25.00,
                        8.00,
                        500,
                        4,
                        'ACTIVE',
                        TIMESTAMP '2026-06-25 09:00:00'
                    WHERE NOT EXISTS (
                        SELECT 1 FROM public.warehouses
                        WHERE warehouse_id = seed_warehouse_id OR warehouse_code = 'WH-SEED-LTL-01'
                    );

                    INSERT INTO public.users
                        (user_id, username, password_hash, email, role_id, full_name, phone, warehouse_id, status, created_at)
                    SELECT
                        receiver_id,
                        'seed.receiver',
                        'seed-not-for-login',
                        'seed.receiver@coldchainx.local',
                        NULL,
                        'Seed Warehouse Receiver',
                        '0900000001',
                        seed_warehouse_id,
                        'ACTIVE',
                        TIMESTAMP '2026-06-25 09:00:00'
                    WHERE NOT EXISTS (
                        SELECT 1 FROM public.users
                        WHERE user_id = receiver_id OR username = 'seed.receiver'
                    );

                    INSERT INTO public.transport_orders
                        (order_id, tracking_code, customer_id, item_name, category, quantity, packing_type, temp_condition,
                         expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, length_cm, width_cm, height_cm,
                         pickup_location, dest_location, cargo_value, status, master_trip_id, route_id, created_at)
                    SELECT
                        order_1_id, 'SEED-LTL-ORD-004', seed_customer_id, 'Vaccine Cold Box 2-8C', 'PHARMA', 24, 'COLD_BOX', '2-8C',
                        180.00, 178.50, 1.2000, 1.1800, 120.00, 100.00, 98.00,
                        pickup_location_id, dest_location_1_id, 150000000.00, 'IN_STOCK', NULL, NULL, TIMESTAMP '2026-06-25 09:05:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE tracking_code = 'SEED-LTL-ORD-004');

                    INSERT INTO public.transport_orders
                        (order_id, tracking_code, customer_id, item_name, category, quantity, packing_type, temp_condition,
                         expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, length_cm, width_cm, height_cm,
                         pickup_location, dest_location, cargo_value, status, master_trip_id, route_id, created_at)
                    SELECT
                        order_2_id, 'SEED-LTL-ORD-005', seed_customer_id, 'Frozen Seafood Cartons', 'SEAFOOD', 36, 'CARTON', '-25--18C',
                        520.00, 515.00, 3.6000, 3.5400, 160.00, 120.00, 155.00,
                        pickup_location_id, dest_location_2_id, 87000000.00, 'IN_STOCK', NULL, NULL, TIMESTAMP '2026-06-25 09:10:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE tracking_code = 'SEED-LTL-ORD-005');

                    INSERT INTO public.transport_orders
                        (order_id, tracking_code, customer_id, item_name, category, quantity, packing_type, temp_condition,
                         expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, length_cm, width_cm, height_cm,
                         pickup_location, dest_location, cargo_value, status, master_trip_id, route_id, created_at)
                    SELECT
                        order_3_id, 'SEED-LTL-ORD-006', seed_customer_id, 'Chilled Yogurt Pallet', 'FOOD', 48, 'PALLET', '2-8C',
                        390.00, 386.00, 2.8000, 2.7600, 120.00, 100.00, 145.00,
                        pickup_location_id, dest_location_3_id, 42000000.00, 'IN_STOCK', NULL, NULL, TIMESTAMP '2026-06-25 09:15:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.transport_orders WHERE tracking_code = 'SEED-LTL-ORD-006');

                    INSERT INTO public.inbound_asn
                        (asn_id, asn_code, order_id, requested_dropoff_time, qr_code_value, status, phone, warehouse_id, customer_id, file_url, created_at)
                    SELECT '77000000-0000-0000-0000-000000000201', 'ASN-SEED-LTL-004', order_1_id, TIMESTAMP '2026-06-25 10:00:00', 'ASN|ASN-SEED-LTL-004|ORDER|SEED-LTL-ORD-004', 'RECEIVED', '0900001001', seed_warehouse_id, seed_customer_id, NULL, TIMESTAMP '2026-06-25 09:20:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.inbound_asn WHERE asn_code = 'ASN-SEED-LTL-004');

                    INSERT INTO public.inbound_asn
                        (asn_id, asn_code, order_id, requested_dropoff_time, qr_code_value, status, phone, warehouse_id, customer_id, file_url, created_at)
                    SELECT '77000000-0000-0000-0000-000000000202', 'ASN-SEED-LTL-005', order_2_id, TIMESTAMP '2026-06-25 10:30:00', 'ASN|ASN-SEED-LTL-005|ORDER|SEED-LTL-ORD-005', 'RECEIVED', '0900001002', seed_warehouse_id, seed_customer_id, NULL, TIMESTAMP '2026-06-25 09:25:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.inbound_asn WHERE asn_code = 'ASN-SEED-LTL-005');

                    INSERT INTO public.inbound_asn
                        (asn_id, asn_code, order_id, requested_dropoff_time, qr_code_value, status, phone, warehouse_id, customer_id, file_url, created_at)
                    SELECT '77000000-0000-0000-0000-000000000203', 'ASN-SEED-LTL-006', order_3_id, TIMESTAMP '2026-06-25 11:00:00', 'ASN|ASN-SEED-LTL-006|ORDER|SEED-LTL-ORD-006', 'RECEIVED', '0900001003', seed_warehouse_id, seed_customer_id, NULL, TIMESTAMP '2026-06-25 09:30:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.inbound_asn WHERE asn_code = 'ASN-SEED-LTL-006');

                    INSERT INTO public.warehouse_receipts
                        (receipt_id, receipt_code, reference_doc_no, order_id, warehouse_id, receipt_type, reason,
                         total_expected_qty, total_actual_qty, recorded_temperature, deliverer_name, receiver_id, note, pdf_url, created_at)
                    SELECT receipt_1_id, 'GRN-SEED-LTL-004', 'ASN-SEED-LTL-004', order_1_id, seed_warehouse_id, 'INBOUND', 'Seed inbound completed',
                        24, 24, 4.20, 'Seed Carrier A', receiver_id, 'Seed: received and ready for put-away.', NULL, TIMESTAMP '2026-06-25 10:15:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.warehouse_receipts WHERE receipt_code = 'GRN-SEED-LTL-004');

                    INSERT INTO public.warehouse_receipts
                        (receipt_id, receipt_code, reference_doc_no, order_id, warehouse_id, receipt_type, reason,
                         total_expected_qty, total_actual_qty, recorded_temperature, deliverer_name, receiver_id, note, pdf_url, created_at)
                    SELECT receipt_2_id, 'GRN-SEED-LTL-005', 'ASN-SEED-LTL-005', order_2_id, seed_warehouse_id, 'INBOUND', 'Seed inbound completed',
                        36, 36, -19.80, 'Seed Carrier B', receiver_id, 'Seed: frozen seafood received and put away.', NULL, TIMESTAMP '2026-06-25 10:45:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.warehouse_receipts WHERE receipt_code = 'GRN-SEED-LTL-005');

                    INSERT INTO public.warehouse_receipts
                        (receipt_id, receipt_code, reference_doc_no, order_id, warehouse_id, receipt_type, reason,
                         total_expected_qty, total_actual_qty, recorded_temperature, deliverer_name, receiver_id, note, pdf_url, created_at)
                    SELECT receipt_3_id, 'GRN-SEED-LTL-006', 'ASN-SEED-LTL-006', order_3_id, seed_warehouse_id, 'INBOUND', 'Seed inbound completed',
                        48, 48, 5.10, 'Seed Carrier C', receiver_id, 'Seed: chilled food received and put away.', NULL, TIMESTAMP '2026-06-25 11:15:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.warehouse_receipts WHERE receipt_code = 'GRN-SEED-LTL-006');

                    INSERT INTO public.lpns
                        (lpn_id, lpn_code, order_id, customer_id, receipt_id, route_id, trip_id, quantity, actual_weight_kg, actual_cbm,
                         length_cm, width_cm, height_cm, required_temperature, recorded_temperature, storage_location, state,
                         discrepancy_reason, evidence_image_url, inbound_time, sla_deadline, created_at, updated_at)
                    SELECT '78000000-0000-0000-0000-000000000201', 'LPN-SEED-LTL-007', order_1_id, seed_customer_id, receipt_1_id, NULL, NULL, 24, 178.50, 1.1800,
                        120.00, 100.00, 98.00, 4.00, 4.20, 'COLD-A1-01', 'IN_STOCK', NULL, NULL, TIMESTAMP '2026-06-25 10:20:00', TIMESTAMP '2026-06-27 10:20:00', TIMESTAMP '2026-06-25 10:20:00', TIMESTAMP '2026-06-25 10:35:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.lpns WHERE lpn_code = 'LPN-SEED-LTL-007');

                    INSERT INTO public.lpns
                        (lpn_id, lpn_code, order_id, customer_id, receipt_id, route_id, trip_id, quantity, actual_weight_kg, actual_cbm,
                         length_cm, width_cm, height_cm, required_temperature, recorded_temperature, storage_location, state,
                         discrepancy_reason, evidence_image_url, inbound_time, sla_deadline, created_at, updated_at)
                    SELECT '78000000-0000-0000-0000-000000000202', 'LPN-SEED-LTL-005', order_2_id, seed_customer_id, receipt_2_id, NULL, NULL, 20, 286.00, 1.9800,
                        160.00, 120.00, 86.00, -18.00, -19.80, 'FROZEN-B2-03', 'IN_STOCK', NULL, NULL, TIMESTAMP '2026-06-25 10:50:00', TIMESTAMP '2026-06-27 10:50:00', TIMESTAMP '2026-06-25 10:50:00', TIMESTAMP '2026-06-25 11:05:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.lpns WHERE lpn_code = 'LPN-SEED-LTL-005');

                    INSERT INTO public.lpns
                        (lpn_id, lpn_code, order_id, customer_id, receipt_id, route_id, trip_id, quantity, actual_weight_kg, actual_cbm,
                         length_cm, width_cm, height_cm, required_temperature, recorded_temperature, storage_location, state,
                         discrepancy_reason, evidence_image_url, inbound_time, sla_deadline, created_at, updated_at)
                    SELECT '78000000-0000-0000-0000-000000000203', 'LPN-SEED-LTL-006', order_2_id, seed_customer_id, receipt_2_id, NULL, NULL, 16, 229.00, 1.5600,
                        150.00, 115.00, 82.00, -18.00, -19.60, 'FROZEN-B2-04', 'IN_STOCK', NULL, NULL, TIMESTAMP '2026-06-25 10:52:00', TIMESTAMP '2026-06-27 10:52:00', TIMESTAMP '2026-06-25 10:52:00', TIMESTAMP '2026-06-25 11:07:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.lpns WHERE lpn_code = 'LPN-SEED-LTL-006');

                    INSERT INTO public.lpns
                        (lpn_id, lpn_code, order_id, customer_id, receipt_id, route_id, trip_id, quantity, actual_weight_kg, actual_cbm,
                         length_cm, width_cm, height_cm, required_temperature, recorded_temperature, storage_location, state,
                         discrepancy_reason, evidence_image_url, inbound_time, sla_deadline, created_at, updated_at)
                    SELECT '78000000-0000-0000-0000-000000000204', 'LPN-SEED-LTL-007', order_3_id, seed_customer_id, receipt_3_id, NULL, NULL, 48, 386.00, 2.7600,
                        120.00, 100.00, 145.00, 5.00, 5.10, 'CHILLED-C1-02', 'IN_STOCK', NULL, NULL, TIMESTAMP '2026-06-25 11:20:00', TIMESTAMP '2026-06-27 11:20:00', TIMESTAMP '2026-06-25 11:20:00', TIMESTAMP '2026-06-25 11:35:00'
                    WHERE NOT EXISTS (SELECT 1 FROM public.lpns WHERE lpn_code = 'LPN-SEED-LTL-007');
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
