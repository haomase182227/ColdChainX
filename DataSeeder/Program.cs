using System;
using System.IO;
using Npgsql;

class Program
{
    static void Main(string[] args)
    {
        string connStr = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";
        string sqlPath = @"c:\Users\ASUS\Music\CN 9\ĐA\ColdChainX\seed_vehicles.sql";
        string sql = File.ReadAllText(sqlPath);

        using (var conn = new NpgsqlConnection(connStr))
        {
            conn.Open();
            using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
                Console.WriteLine("Data inserted successfully.");
            }
        }
    }
}
