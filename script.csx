#r "nuget: Npgsql, 8.0.3"

using System;
using System.Collections.Generic;
using Npgsql;

string connStr = "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";

using (var conn = new NpgsqlConnection(connStr))
{
    conn.Open();

    var findTablesSql = @"
    SELECT table_name, column_name 
    FROM information_schema.columns 
    WHERE table_schema = 'public' AND (column_name = 'CreatedAt' OR column_name = 'created_at')
    ";

    var tables = new List<(string Table, string Column)>();
    using (var cmd = new NpgsqlCommand(findTablesSql, conn))
    {
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
            }
        }
    }

    Console.WriteLine($"Found {tables.Count} tables with CreatedAt column.");

    bool deletedAny = true;
    int maxLoops = 10;
    int loopCount = 0;

    // Chạy lặp lại vài lần để xử lý lỗi dính khóa ngoại (Foreign Keys)
    while (deletedAny && loopCount < maxLoops)
    {
        deletedAny = false;
        loopCount++;
        Console.WriteLine($"\n--- Loop {loopCount} ---");

        foreach (var t in tables)
        {
            // Sửa lại ngày để xóa dữ liệu theo ý muốn
            var checkSql = $"SELECT COUNT(*) FROM \"{t.Table}\" WHERE \"{t.Column}\" >= '2026-08-04' AND \"{t.Column}\" < '2026-08-08'";
            using (var checkCmd = new NpgsqlCommand(checkSql, conn))
            {
                try 
                {
                    var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                    if (count > 0)
                    {
                        var deleteSql = $"DELETE FROM \"{t.Table}\" WHERE \"{t.Column}\" >= '2026-08-04' AND \"{t.Column}\" < '2026-08-08'";
                        using (var deleteCmd = new NpgsqlCommand(deleteSql, conn))
                        {
                            var deleted = deleteCmd.ExecuteNonQuery();
                            Console.WriteLine($"-> Deleted {deleted} rows from {t.Table}.");
                            deletedAny = true;
                        }
                    }
                } 
                catch (Exception) 
                {
                    // Tạm thời bỏ qua các lỗi do khóa ngoại, các vòng lặp sau có thể sẽ giải quyết được
                }
            }
        }
    }
    
    Console.WriteLine("Done.");
}
