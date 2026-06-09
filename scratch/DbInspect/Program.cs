using System;
using Npgsql;

class Program
{
    static void Main()
    {
        string connString = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026";
        using var conn = new NpgsqlConnection(connString);
        conn.Open();

        Console.WriteLine("--- CURRENT TABLES AND ROW COUNTS ---");
        using var cmd = new NpgsqlCommand(@"
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            ORDER BY table_name;", conn);

        var tables = new System.Collections.Generic.List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            using var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table}", conn);
            Console.WriteLine($"{table} : {countCmd.ExecuteScalar()} rows");
        }
    }
}
