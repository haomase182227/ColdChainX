using System;
using Npgsql;

class Program
{
    static void Main()
    {
        string connString = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026";
        using var conn = new NpgsqlConnection(connString);
        conn.Open();

        PrintTableInfo(conn, "roles");
        PrintTableInfo(conn, "users");
    }

    static void PrintTableInfo(NpgsqlConnection conn, string tableName)
    {
        Console.WriteLine($"\n--- Columns of {tableName} ---");
        using (var cmd = new NpgsqlCommand($@"
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_name = '{tableName}' 
            ORDER BY ordinal_position;", conn))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                Console.WriteLine($"{reader.GetString(0)} ({reader.GetString(1)})");
            }
        }

        Console.WriteLine($"\n--- Rows of {tableName} ---");
        using (var cmd = new NpgsqlCommand($"SELECT * FROM public.{tableName} LIMIT 20", conn))
        using (var reader = cmd.ExecuteReader())
        {
            int fieldCount = reader.FieldCount;
            for (int i = 0; i < fieldCount; i++)
            {
                Console.Write(reader.GetName(i) + "\t");
            }
            Console.WriteLine();
            while (reader.Read())
            {
                for (int i = 0; i < fieldCount; i++)
                {
                    Console.Write(reader.GetValue(i) + "\t");
                }
                Console.WriteLine();
            }
        }
    }
}


