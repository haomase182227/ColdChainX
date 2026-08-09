using System;
using System.Threading.Tasks;
using Npgsql;

namespace CheckLocationApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string connString = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";
            using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_name = 'iot_devices';", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                Console.WriteLine(reader.GetString(0));
            }
        }
    }
}
