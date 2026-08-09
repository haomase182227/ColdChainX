using Npgsql;

var connStr = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";

using var conn = new NpgsqlConnection(connStr);
conn.Open();
Console.WriteLine("=== Connected to DB ===");

Console.WriteLine("\n--- ALL DISTINCT status VALUES ---");
using (var cmd = new NpgsqlCommand(@"
    SELECT status, COUNT(*) as cnt 
    FROM transport_orders 
    GROUP BY status 
    ORDER BY cnt DESC", conn))
using (var reader = cmd.ExecuteReader())
{
    while (reader.Read())
        Console.WriteLine($"  status='{reader.GetString(0)}' => count={reader.GetInt64(1)}");
}

Console.WriteLine("\n--- ALL DISTINCT customer_id VALUES (with order count) ---");
using (var cmd = new NpgsqlCommand(@"
    SELECT customer_id::text, COUNT(*) as cnt 
    FROM transport_orders 
    GROUP BY customer_id 
    ORDER BY cnt DESC", conn))
using (var reader = cmd.ExecuteReader())
{
    while (reader.Read())
    {
        var custId = reader.IsDBNull(0) ? "NULL" : reader.GetString(0);
        Console.WriteLine($"  customer_id={custId} => orders={reader.GetInt64(1)}");
    }
}

Console.WriteLine("\n--- customer_id x status CROSS (non-null customers only) ---");
using (var cmd = new NpgsqlCommand(@"
    SELECT customer_id::text, status, COUNT(*) as cnt 
    FROM transport_orders 
    WHERE customer_id IS NOT NULL
    GROUP BY customer_id, status 
    ORDER BY customer_id, status", conn))
using (var reader = cmd.ExecuteReader())
{
    while (reader.Read())
        Console.WriteLine($"  customer={reader.GetString(0)} status='{reader.GetString(1)}' count={reader.GetInt64(2)}");
}

Console.WriteLine("\n=== DONE ===");
