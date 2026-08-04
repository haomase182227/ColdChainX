using System.Text.Json;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Map function codes → API endpoints.
/// Loaded from endpoint_map.json for easy customization.
/// </summary>
public class EndpointMapper
{
    private readonly Dictionary<string, EndpointInfo> _map = new(StringComparer.OrdinalIgnoreCase);

    public static EndpointMapper LoadFromFile(string jsonPath)
    {
        var mapper = new EndpointMapper();
        if (!File.Exists(jsonPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ endpoint_map.json not found at {jsonPath}, using built-in defaults.");
            Console.ResetColor();
            mapper.LoadDefaults();
            return mapper;
        }

        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var ep = new EndpointInfo
            {
                Method = prop.Value.GetProperty("method").GetString() ?? "GET",
                Url = prop.Value.GetProperty("url").GetString() ?? "",
                AuthRole = prop.Value.TryGetProperty("auth", out var auth) ? auth.GetString() ?? "Anonymous" : "Anonymous",
                BodyType = prop.Value.TryGetProperty("body", out var body) ? body.GetString() ?? "None" : "None",
                Notes = prop.Value.TryGetProperty("notes", out var notes) ? notes.GetString() : null,
            };
            mapper._map[prop.Name] = ep;
        }
        return mapper;
    }

    public EndpointInfo? Get(string functionCode)
        => _map.TryGetValue(functionCode, out var info) ? info : null;

    public int Count => _map.Count;

    private void LoadDefaults()
    {
        // AUTH
        _map["AUTH001"] = new("POST", "/api/auth/create-customer", "Anonymous", "Form");
        _map["AUTH002"] = new("POST", "/api/auth/create-driver", "Admin", "Form");
        _map["AUTH003"] = new("POST", "/api/auth/create-warehouse-worker", "Anonymous", "Form");
        _map["AUTH004"] = new("POST", "/api/auth/login", "Anonymous", "Json");
        _map["AUTH005"] = new("POST", "/api/auth/google-login", "Anonymous", "Json");
        _map["AUTH006"] = new("PUT", "/api/auth/change-password", "Any", "Json");

        // USER
        _map["USR001"] = new("GET", "/api/v1/users", "Admin");
        _map["USR002"] = new("GET", "/api/v1/users/{{userId}}", "Admin");
        _map["USR003"] = new("POST", "/api/v1/users", "Admin", "Json");
        _map["USR004"] = new("PUT", "/api/v1/users/{{userId}}", "Admin", "Json");
        _map["USR005"] = new("PUT", "/api/v1/users/{{userId}}/role", "Admin", "Json");
        _map["USR006"] = new("PUT", "/api/v1/users/{{userId}}/status", "Admin", "Json");
        _map["USR007"] = new("PUT", "/api/v1/users/{{userId}}/warehouse", "Admin", "Json");
        _map["USR008"] = new("POST", "/api/v1/users/{{userId}}/reset-password", "Admin");
        _map["USR009"] = new("DELETE", "/api/v1/users/{{userId}}", "Admin");
        _map["USR010"] = new("POST", "/api/v1/users/{{userId}}/restore", "Admin");

        // CUSTOMER
        _map["CUS001"] = new("GET", "/api/customers", "Admin");
        _map["CUS002"] = new("GET", "/api/customers/{{customerId}}", "Admin");

        // CHAT
        _map["CHT001"] = new("POST", "/api/chat/conversations", "Any", "Json");
        _map["CHT002"] = new("GET", "/api/chat/conversations", "Any");
        _map["CHT003"] = new("GET", "/api/chat/conversations/{{conversationId}}/messages", "Any");
        _map["CHT004"] = new("POST", "/api/chat/conversations/{{conversationId}}/messages", "Any", "Json");
        _map["CHT005"] = new("POST", "/api/chat/conversations/{{conversationId}}/read", "Any");

        // SERVICE CATALOG
        _map["CAT001"] = new("GET", "/api/service-catalogs", "Any");
        _map["CAT002"] = new("GET", "/api/service-catalogs/{{catalogId}}", "Any");
        _map["CAT003"] = new("POST", "/api/service-catalogs", "Admin", "Json");
        _map["CAT004"] = new("PUT", "/api/service-catalogs/{{catalogId}}", "Admin", "Json");
        _map["CAT005"] = new("DELETE", "/api/service-catalogs/{{catalogId}}", "Admin");
        _map["CAT006"] = new("GET", "/api/service-catalogs/active", "Any");

        // WEIGHT TIER
        _map["WTR001"] = new("GET", "/api/weight-tiers", "Any");
        _map["WTR002"] = new("GET", "/api/weight-tiers/{{tierId}}", "Any");
        _map["WTR003"] = new("POST", "/api/weight-tiers", "Admin", "Json");
        _map["WTR004"] = new("PUT", "/api/weight-tiers/{{tierId}}", "Admin", "Json");
        _map["WTR005"] = new("DELETE", "/api/weight-tiers/{{tierId}}", "Admin");
        _map["WTR006"] = new("GET", "/api/weight-tiers/calculate", "Any");
        _map["WTR007"] = new("POST", "/api/weight-tiers/import", "Admin", "Form");

        // ROUTE
        _map["ROU001"] = new("GET", "/api/routes/options", "Any");
        _map["ROU002"] = new("GET", "/api/routes/{{routeId}}/detail", "Any");
        _map["ROU003"] = new("POST", "/api/routes", "Any", "Json");
        _map["ROU004"] = new("PUT", "/api/routes/{{routeId}}", "Any", "Json");
        _map["ROU005"] = new("DELETE", "/api/routes/{{routeId}}", "Any");
        _map["ROU006"] = new("GET", "/api/routes/{{routeId}}/booking-options", "Any");
        _map["ROU007"] = new("GET", "/api/routes/{{routeId}}/origin-warehouses", "Any");
        _map["ROU008"] = new("GET", "/api/routes/{{routeId}}/schedules", "Any");
        _map["ROU009"] = new("POST", "/api/routes/{{routeId}}/schedules", "Any", "Json");
        _map["ROU010"] = new("PUT", "/api/routes/schedules/{{scheduleId}}", "Any", "Json");
        _map["ROU011"] = new("DELETE", "/api/routes/schedules/{{scheduleId}}", "Any");
        _map["ROU012"] = new("GET", "/api/routes/{{routeId}}/stops", "Any");
        _map["ROU013"] = new("POST", "/api/routes/{{routeId}}/stops", "Any", "Json");
        _map["ROU014"] = new("PUT", "/api/routes/stops/{{stopId}}", "Any", "Json");

        // QUOTATION
        _map["QOT001"] = new("POST", "/api/quotations", "Admin", "Json");
        _map["QOT002"] = new("GET", "/api/quotations", "Any");
        _map["QOT003"] = new("GET", "/api/quotations/{{quotationId}}", "Any");
        _map["QOT004"] = new("GET", "/api/orders/{{orderId}}/quotations", "Any");
        _map["QOT005"] = new("GET", "/api/customers/{{customerId}}/quotations", "Any");
        _map["QOT006"] = new("PUT", "/api/quotations/{{quotationId}}/approve", "Admin", "Json");
        _map["QOT007"] = new("PUT", "/api/quotations/{{quotationId}}/reject", "Admin", "Json");
        _map["QOT008"] = new("PUT", "/api/quotations/{{quotationId}}/cancel", "Any", "Json");
        _map["QOT009"] = new("PUT", "/api/quotations/{{quotationId}}/review", "Admin", "Json");

        // CONTRACT
        _map["CTR001"] = new("POST", "/api/contracts", "Admin", "Json");
        _map["CTR002"] = new("GET", "/api/contracts", "Any");
        _map["CTR003"] = new("GET", "/api/contracts/{{contractId}}", "Any");
        _map["CTR004"] = new("GET", "/api/contracts/order/{{orderId}}", "Any");
        _map["CTR005"] = new("GET", "/api/contracts/customer/{{customerId}}", "Any");
        _map["CTR006"] = new("POST", "/api/contracts/{{contractId}}/send", "Admin");
        _map["CTR007"] = new("POST", "/api/contracts/{{contractId}}/upload-signed", "Customer", "Form");
        _map["CTR008"] = new("POST", "/api/contracts/{{contractId}}/verify", "Admin", "Json");
        _map["CTR009"] = new("POST", "/api/contracts/{{contractId}}/reject", "Admin", "Json");
        _map["CTR010"] = new("POST", "/api/contracts/{{contractId}}/cancel", "Any", "Json");
        _map["CTR011"] = new("GET", "/api/contracts/{{contractId}}/preview", "Any");
        _map["CTR012"] = new("GET", "/api/contract-appendices/contract/{{contractId}}", "Any");
        _map["CTR013"] = new("POST", "/api/contract-appendices", "Admin", "Json");
        _map["CTR014"] = new("POST", "/api/contract-appendices/{{appendixId}}/send", "Admin");
        _map["CTR015"] = new("POST", "/api/contract-appendices/{{appendixId}}/upload-signed", "Customer", "Form");
        _map["CTR016"] = new("POST", "/api/contract-appendices/{{appendixId}}/verify", "Admin", "Json");
        _map["CTR017"] = new("POST", "/api/contract-appendices/{{appendixId}}/reject", "Admin", "Json");

        // ORDER
        _map["ORD001"] = new("POST", "/api/orders", "Customer", "Form");
        _map["ORD002"] = new("PUT", "/api/orders/{{orderId}}", "Customer", "Form");
        _map["ORD003"] = new("POST", "/api/orders/{{orderId}}/cancel", "Customer", "Json");
        _map["ORD004"] = new("POST", "/api/orders/{{orderId}}/review", "Admin", "Json");
        _map["ORD005"] = new("POST", "/api/orders/{{orderId}}/assign-route", "Admin", "Json");
        _map["ORD006"] = new("GET", "/api/orders", "Any");
        _map["ORD007"] = new("GET", "/api/orders/my-orders", "Customer");
        _map["ORD008"] = new("GET", "/api/orders/{{orderId}}", "Any");
        _map["ORD009"] = new("GET", "/api/orders/track/{{trackingCode}}", "Any");
        _map["ORD010"] = new("GET", "/api/orders/chart/overview", "Admin");
        _map["ORD011"] = new("GET", "/api/orders/{{orderId}}/origin-warehouses", "Any");

        // INBOUND
        _map["INB001"] = new("POST", "/api/inbound/asn", "Any", "Json");
        _map["INB002"] = new("GET", "/api/inbound/schedules", "Any");
        _map["INB003"] = new("GET", "/api/inbound/asn/{{asnId}}", "Any");
        _map["INB004"] = new("POST", "/api/inbound/asn/{{asnId}}/scan-qr", "Any", "Json");
        _map["INB005"] = new("POST", "/api/inbound/receipts", "Any", "Json");
        _map["INB006"] = new("POST", "/api/inbound/receipts/{{receiptId}}/putaway", "Any", "Json");
        _map["INB007"] = new("GET", "/api/inbound/receipts/{{receiptId}}", "Any");
        _map["INB008"] = new("GET", "/api/inbound/receipts", "Any");

        // OUTBOUND
        _map["OUT001"] = new("POST", "/api/outbound", "Any", "Json");
        _map["OUT002"] = new("GET", "/api/outbound", "Any");
        _map["OUT003"] = new("GET", "/api/outbound/{{outboundId}}", "Any");
        _map["OUT004"] = new("POST", "/api/outbound/{{outboundId}}/approve", "Admin", "Json");
        _map["OUT005"] = new("POST", "/api/outbound/{{outboundId}}/cancel", "Any", "Json");
    }
}
