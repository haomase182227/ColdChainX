using System;
using System.IO;
using Npgsql;

class Program
{
    static void Main()
    {
        string connString = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";
        Console.WriteLine("Connecting to DB: coldchainx-db-server.postgres.database.azure.com...");
        using var conn = new NpgsqlConnection(connString);
        conn.Open();
        Console.WriteLine("Connected successfully!");

        // To run schema cleaning and ERD.txt migration, uncomment the following line:
        // RunMigration(conn);
        
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

    static void RunMigration(NpgsqlConnection conn)
    {
        // 1. Clean the schema
        Console.WriteLine("Cleaning database (dropping and recreating public schema)...");
        using (var cleanCmd = new NpgsqlCommand(@"
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public;
            GRANT ALL ON SCHEMA public TO postgres;
            GRANT ALL ON SCHEMA public TO public;
        ", conn))
        {
            cleanCmd.ExecuteNonQuery();
        }
        Console.WriteLine("Database schema cleaned.");

        // 2. Read ERD.txt
        string erdPath = @"c:\Users\Lenovo\Downloads\ColdChainX\ERD.txt";
        Console.WriteLine($"Reading SQL from {erdPath}...");
        if (!File.Exists(erdPath))
        {
            Console.WriteLine("Error: ERD.txt not found!");
            return;
        }
        string sql = File.ReadAllText(erdPath);

        // 3. Execute ERD.txt SQL
        Console.WriteLine("Executing ERD.txt SQL migration...");
        using (var migrateCmd = new NpgsqlCommand(sql, conn))
        {
            migrateCmd.CommandTimeout = 300; // 5 minutes timeout
            migrateCmd.ExecuteNonQuery();
        }
        Console.WriteLine("Migration executed successfully!");

        // 4. Verify tables and counts
        Console.WriteLine("\n--- VERIFYING CREATED TABLES AND ROW COUNTS ---");
        using var verifyCmd = new NpgsqlCommand(@"
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            ORDER BY table_name;", conn);

        var tables = new System.Collections.Generic.List<string>();
        using (var reader = verifyCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            using var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{table}\"", conn);
            Console.WriteLine($"\"{table}\" : {countCmd.ExecuteScalar()} rows");
        }
    }
}
