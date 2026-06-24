using System;
using Npgsql;

class Program
{
    static void Main()
    {
        string connString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";
        Console.WriteLine("Connecting to local DB to update notification template...");
        using var conn = new NpgsqlConnection(connString);
        conn.Open();
        Console.WriteLine("Connected!");

        // Update the template body_template to include Appendix info
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE notification_templates 
                SET body_template = 'Phát hiện chênh lệch >5% tại Inbound QC. Biên bản bất thường: {{Pdf_URL}}. Phụ lục hợp đồng nháp: {{Appendix_Number}} (ID: {{Appendix_Id}})' 
                WHERE template_id = 'NOTI_QC_DISCREPANCY';";
            
            int rowsAffected = cmd.ExecuteNonQuery();
            Console.WriteLine($"Updated NOTI_QC_DISCREPANCY template. Rows affected: {rowsAffected}");
        }

        // Query the template to verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT template_id, body_template FROM notification_templates WHERE template_id = 'NOTI_QC_DISCREPANCY';";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                Console.WriteLine($"Template ID: {reader["template_id"]}\nBody Template: {reader["body_template"]}");
            }
            else
            {
                Console.WriteLine("Template not found.");
            }
        }
    }
}
