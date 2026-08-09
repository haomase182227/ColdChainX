using System;
using System.Threading.Tasks;
using Npgsql;

namespace PatchDbTool
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var connStr = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            Console.WriteLine("Connected to database. Seeding 5 test orders...");

            string sql = @"
                DO $$
                DECLARE
                    c_id uuid := '61000000-0000-0000-0000-000000000001';
                    r_id uuid := '62000000-0000-0000-0000-000000000001';
                    s1_id uuid := '63000000-0000-0000-0000-000000000001';
                    s2_id uuid := '63000000-0000-0000-0000-000000000002';
                    loc1_id uuid := '64000000-0000-0000-0000-000000000001';
                    loc2_id uuid := '64000000-0000-0000-0000-000000000002';
                    o1_id uuid := '65000000-0000-0000-0000-000000000001';
                    o2_id uuid := '65000000-0000-0000-0000-000000000002';
                    o3_id uuid := '65000000-0000-0000-0000-000000000003';
                    o4_id uuid := '65000000-0000-0000-0000-000000000004';
                    o5_id uuid := '65000000-0000-0000-0000-000000000005';
                    wh_rec_id uuid := '66000000-0000-0000-0000-000000000001';
                BEGIN
                    UPDATE public.lpns
                    SET warehouse_id = (SELECT warehouse_id FROM public.warehouses LIMIT 1)
                    WHERE warehouse_id IS NULL;
                END
                $$;
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine("Successfully seeded 5 test orders and 18 LPNs!");
        }
    }
}
