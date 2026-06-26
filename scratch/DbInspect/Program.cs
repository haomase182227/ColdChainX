using System;
using Npgsql;

class Program
{
    static void Main(string[] args)
    {
        string connString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";
        Console.WriteLine("[*] Connecting to local DB...");
        using var conn = new NpgsqlConnection(connString);
        conn.Open();
        Console.WriteLine("[+] Connected.");

        bool reset = args.Length > 0 && args[0].ToLower() == "reset";

        if (reset)
        {
            Console.WriteLine("[*] Resetting test delivery data...");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    -- 1. Delete delivery confirmations
                    DELETE FROM public.lpn_delivery_confirmations 
                    WHERE trip_id = '77777777-7777-7777-7777-777777777777';

                    -- 2. Delete test seals applied during delivery
                    DELETE FROM public.seals 
                    WHERE trip_id = '77777777-7777-7777-7777-777777777777' 
                      AND note LIKE '%Applied during delivery%';

                    -- 3. Restore older seals if they were cancelled
                    UPDATE public.seals 
                    SET status = 'APPLIED', removed_at = NULL 
                      WHERE trip_id = '77777777-7777-7777-7777-777777777777' 
                      AND status = 'CANCELLED';

                    -- 4. Delete trip stops for this trip and insert a fresh one
                    DELETE FROM public.trip_stops WHERE trip_id = '77777777-7777-7777-7777-777777777777';
                    INSERT INTO public.trip_stops (
                        stop_id, trip_id, location_id, stop_sequence, stop_type, planned_arrival_time, planned_departure_time, status, created_at
                    ) VALUES (
                        '66666666-6666-6666-6666-666666666666',
                        '77777777-7777-7777-7777-777777777777',
                        'eb630bab-797e-401d-be91-b2d5979717f3',
                        1,
                        'DELIVERY',
                        NOW(),
                        NOW() + INTERVAL '30 minutes',
                        'PLANNED',
                        NOW()
                    );

                    -- 5. Update destination location to match user home address and coordinates
                    UPDATE public.locations 
                    SET address = '568/58 le van viet tp HCM',
                        latitude = 10.8465,
                        longitude = 106.8042
                    WHERE location_id = 'eb630bab-797e-401d-be91-b2d5979717f3';

                    -- 6. Reset LPN states to SHIPPING (8)
                    UPDATE public.lpns 
                    SET state = 8, evidence_image_url = NULL, recorded_temperature = NULL 
                    WHERE trip_id = '77777777-7777-7777-7777-777777777777';

                    -- 7. Reset Trip status to DISPATCHED
                    UPDATE public.master_trips 
                    SET status = 'DISPATCHED', completed_at = NULL 
                    WHERE trip_id = '77777777-7777-7777-7777-777777777777';

                    -- 8. Reset Order status to SHIPPING and set cargo value to 2000
                    UPDATE public.transport_orders 
                    SET status = 'SHIPPING',
                        cargo_value = 2000.00
                    WHERE order_id = '56c5cf5b-e403-4ab7-bc8e-b686dea7675b';
                ";
                int rows = cmd.ExecuteNonQuery();
                Console.WriteLine($"[+] Reset completed. {rows} rows affected.");
            }
        }

        // Query master_trips
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT trip_id, status FROM public.master_trips WHERE trip_id = '77777777-7777-7777-7777-777777777777'";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n--- TRIPS IN DATABASE ---");
            while (reader.Read())
            {
                Console.WriteLine($"TripId: {reader["trip_id"]} | Status: {reader["status"]}");
            }
        }

        // Query lpns
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT lpn_id, lpn_code, state, trip_id FROM public.lpns WHERE trip_id = '77777777-7777-7777-7777-777777777777'";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n--- LPNS IN DATABASE ---");
            while (reader.Read())
            {
                Console.WriteLine($"LpnId: {reader["lpn_id"]} | Code: {reader["lpn_code"]} | State: {reader["state"]} | TripId: {reader["trip_id"]}");
            }
        }

        // Query transport_orders and their destination locations
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT o.order_id, o.tracking_code, o.status, o.dest_location, l.address, l.latitude, l.longitude 
                FROM public.transport_orders o
                LEFT JOIN public.locations l ON o.dest_location = l.location_id
                WHERE o.order_id = '56c5cf5b-e403-4ab7-bc8e-b686dea7675b'";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n--- ORDERS & DESTINATION LOCATIONS IN DATABASE ---");
            while (reader.Read())
            {
                Console.WriteLine($"OrderId: {reader["order_id"]} | Tracking: {reader["tracking_code"]} | Status: {reader["status"]} | DestLocationId: {reader["dest_location"]} | Address: {reader["address"]} | Lat: {reader["latitude"]} | Lon: {reader["longitude"]}");
            }
        }

        // Query trip_stops for the trip
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT ts.stop_id, ts.trip_id, ts.location_id, ts.stop_sequence, ts.stop_type, ts.actual_arrival_time, ts.status, l.address 
                FROM public.trip_stops ts
                LEFT JOIN public.locations l ON ts.location_id = l.location_id
                ORDER BY ts.trip_id, ts.stop_sequence";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("\n--- TRIP STOPS IN DATABASE ---");
            while (reader.Read())
            {
                Console.WriteLine($"TripId: {reader["trip_id"]} | StopId: {reader["stop_id"]} | LocationId: {reader["location_id"]} | Sequence: {reader["stop_sequence"]} | Type: {reader["stop_type"]} | ActualArrival: {reader["actual_arrival_time"]} | Status: {reader["status"]} | Address: {reader["address"]}");
            }
        }
    }
}
