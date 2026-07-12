using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Seed10OrdersForTripBuilding : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    seed_customer_id uuid := '71000000-0000-0000-0000-000000000001';
                    pickup_location_id uuid := '72000000-0000-0000-0000-000000000001';
                    dest_location_1_id uuid := '72000000-0000-0000-0000-000000000101';
                    seed_warehouse_id uuid := '73000000-0000-0000-0000-000000000001';
                    receiver_id uuid := '74000000-0000-0000-0000-000000000001';
                    i int;
                    new_order_id uuid;
                    new_receipt_id uuid;
                    new_asn_id uuid;
                    new_lpn_id uuid;
                BEGIN
                    FOR i IN 1..10 LOOP
                        new_order_id := gen_random_uuid();
                        new_receipt_id := gen_random_uuid();
                        new_asn_id := gen_random_uuid();
                        new_lpn_id := gen_random_uuid();

                        INSERT INTO public.transport_orders
                            (order_id, tracking_code, customer_id, item_name, category, quantity, packing_type, temp_condition,
                             has_strong_odor, is_stackable, pickup_location, dest_location, status, created_at)
                        VALUES
                            (new_order_id, 'SEED-TEST-ORD-' || LPAD(i::text, 3, '0'), seed_customer_id, 'Frozen Meat ' || i, 'FROZEN_FOOD', 50, 'CARTON', '-10',
                             false, true, pickup_location_id, dest_location_1_id, 'IN_STOCK', NOW());

                        INSERT INTO public.order_dimensions
                            (order_id, expected_weight_kg, actual_weight_kg, expected_cbm, actual_cbm, length_cm, width_cm, height_cm)
                        VALUES
                            (new_order_id, 200.0, 200.0, 1.5, 1.5, 100, 100, 150);

                        INSERT INTO public.inbound_asn
                            (asn_id, asn_code, order_id, requested_dropoff_time, qr_code_value, status, phone, warehouse_id, customer_id, created_at)
                        VALUES
                            (new_asn_id, 'ASN-SEED-TEST-' || LPAD(i::text, 3, '0'), new_order_id, NOW(), 'ASN|ASN-SEED-' || i, 'RECEIVED', '0900000000', seed_warehouse_id, seed_customer_id, NOW());

                        INSERT INTO public.warehouse_receipts
                            (receipt_id, receipt_code, reference_doc_no, order_id, warehouse_id, receipt_type, reason,
                             total_expected_qty, total_actual_qty, recorded_temperature, deliverer_name, receiver_id, note, created_at)
                        VALUES
                            (new_receipt_id, 'GRN-SEED-TEST-' || LPAD(i::text, 3, '0'), 'ASN-SEED-TEST-' || LPAD(i::text, 3, '0'), new_order_id, seed_warehouse_id, 'INBOUND', 'Seeded',
                             50, 50, -10.0, 'Test Carrier', receiver_id, 'Seeded test data', NOW());

                        INSERT INTO public.lpns
                            (lpn_id, lpn_code, order_id, customer_id, receipt_id, warehouse_id, quantity, actual_weight_kg, actual_cbm,
                             length_cm, width_cm, height_cm, required_temperature, recorded_temperature, storage_location, state,
                             inbound_time, sla_deadline, created_at, updated_at)
                        VALUES
                            (new_lpn_id, 'LPN-SEED-TEST-' || LPAD(i::text, 3, '0'), new_order_id, seed_customer_id, new_receipt_id, seed_warehouse_id, 50, 200.0, 1.5,
                             100, 100, 150, -10.0, -10.0, 'TEST-ZONE-' || i, 'IN_STOCK',
                             NOW(), NOW() + interval '2 days', NOW(), NOW());
                    END LOOP;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM public.lpns WHERE lpn_code LIKE 'LPN-SEED-TEST-%'");
            migrationBuilder.Sql("DELETE FROM public.warehouse_receipts WHERE receipt_code LIKE 'GRN-SEED-TEST-%'");
            migrationBuilder.Sql("DELETE FROM public.inbound_asn WHERE asn_code LIKE 'ASN-SEED-TEST-%'");
            migrationBuilder.Sql("DELETE FROM public.order_dimensions WHERE order_id IN (SELECT order_id FROM public.transport_orders WHERE tracking_code LIKE 'SEED-TEST-ORD-%')");
            migrationBuilder.Sql("DELETE FROM public.transport_orders WHERE tracking_code LIKE 'SEED-TEST-ORD-%'");
        }
    }
}
