using System.Text.Json;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

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
        _map["AUTH001"] = new("POST", "/api/auth/create-customer", "Anonymous", "Form");
        _map["AUTH002"] = new("POST", "/api/auth/create-driver", "Admin", "Form");
        _map["AUTH003"] = new("POST", "/api/auth/create-warehouse-worker", "Admin", "Form");
        _map["AUTH004"] = new("POST", "/api/auth/login", "Anonymous", "Json");
        _map["AUTH006"] = new("PUT", "/api/auth/change-password", "Any", "Json");

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

        _map["CUS001"] = new("GET", "/api/customers", "Admin");
        _map["CUS002"] = new("GET", "/api/customers/{{customerId}}", "Admin");

        _map["CHT001"] = new("GET", "/api/chat/customers?search=test", "Any");
        _map["CHT002"] = new("GET", "/api/chat/{{orderId}}/messages?pageNumber=999&pageSize=10", "Any");
        _map["CHT003"] = new("GET", "/api/chat/customers/{{customerId}}/messages", "Any");
        _map["CHT004"] = new("POST", "/api/chat/{{orderId}}/messages", "Any", "Json");
        _map["CHT005"] = new("PATCH", "/api/chat/{{orderId}}/messages/read", "Any");

        _map["CAT001"] = new("GET", "/api/service-catalogs", "Any");
        _map["CAT002"] = new("GET", "/api/service-catalogs/active", "Anonymous");
        _map["CAT003"] = new("GET", "/api/service-catalogs/{{catalogId}}", "Any");
        _map["CAT004"] = new("POST", "/api/service-catalogs", "Admin", "Json");
        _map["CAT005"] = new("PUT", "/api/service-catalogs/{{catalogId}}", "Admin", "Json");
        _map["CAT006"] = new("DELETE", "/api/service-catalogs/{{catalogId}}", "Admin");

        _map["WTR001"] = new("GET", "/api/weight-tiers", "Any");
        _map["WTR002"] = new("GET", "/api/weight-tiers/{{tierId}}", "Any");
        _map["WTR003"] = new("GET", "/api/routes/{{routeId}}/weight-tiers", "Any");
        _map["WTR004"] = new("POST", "/api/weight-tiers", "Admin", "Json");
        _map["WTR005"] = new("PUT", "/api/weight-tiers/{{tierId}}", "Admin", "Json");
        _map["WTR006"] = new("DELETE", "/api/weight-tiers/{{deleteTierId}}", "Admin");
        _map["WTR007"] = new("POST", "/api/weight-tiers/import", "Admin", "Form");

        _map["ROU001"] = new("GET", "/api/routes/options?originCity=Ho Chi Minh City&destCity=Da Nang", "Any");
        _map["ROU002"] = new("GET", "/api/routes/99999999-9999-9999-9999-999999999999/detail", "Any");
        _map["ROU003"] = new("POST", "/api/routes", "Admin", "Json");
        _map["ROU004"] = new("PUT", "/api/routes/99999999-9999-9999-9999-999999999999", "Admin", "Json");
        _map["ROU005"] = new("DELETE", "/api/routes/{{deleteRouteId}}", "Admin");
        _map["ROU006"] = new("GET", "/api/routes/{{routeId}}/stops", "Any");
        _map["ROU007"] = new("POST", "/api/routes/{{routeId}}/stops", "Admin", "Json");
        _map["ROU008"] = new("PUT", "/api/routes/{{routeId}}/stops/{{stopId}}", "Admin", "Json");
        _map["ROU009"] = new("DELETE", "/api/routes/{{routeId}}/stops/{{deleteStopId}}", "Admin");
        _map["ROU010"] = new("GET", "/api/routes/{{routeId}}/origin-warehouses", "Any");
        _map["ROU011"] = new("GET", "/api/routes/{{routeId}}/schedules", "Any");
        _map["ROU012"] = new("POST", "/api/routes/{{routeId}}/schedules", "Admin", "Json");
        _map["ROU013"] = new("PUT", "/api/routes/{{routeId}}/schedules/{{scheduleId}}", "Admin", "Json");
        _map["ROU014"] = new("DELETE", "/api/routes/{{routeId}}/schedules/{{deleteScheduleId}}", "Admin");
        _map["ROU015"] = new("GET", "/api/routes/{{routeId}}/booking-options", "Any");

        _map["QOT001"] = new("POST", "/api/quotations", "Any", "Json");
        _map["QOT002"] = new("GET", "/api/quotations", "Driver");
        _map["QOT003"] = new("GET", "/api/quotations/{{quoteId}}", "Any");
        _map["QOT004"] = new("GET", "/api/orders/{{orderId}}/quotations", "Any");
        _map["QOT005"] = new("GET", "/api/customers/{{customerId}}/quotations", "Anonymous");
        _map["QOT006"] = new("POST", "/api/quotations", "Admin", "Json");
        _map["QOT007"] = new("PUT", "/api/quotations/{{quoteId}}", "Sales", "Json");
        _map["QOT008"] = new("POST", "/api/quotations/{{quoteId}}/send", "Sales");
        _map["QOT009"] = new("POST", "/api/quotations/{{quoteId}}/accept", "Customer", "Json");

        _map["CTR001"] = new("POST", "/api/contracts/generate", "Admin", "Json");
        _map["CTR002"] = new("GET", "/api/contracts/{{contractId}}", "Any");
        _map["CTR003"] = new("GET", "/api/contracts/by-order/{{orderId}}", "Any");
        _map["CTR004"] = new("GET", "/api/contracts/preview/{{orderId}}", "Any");
        _map["CTR005"] = new("PUT", "/api/contracts/{{contractId}}", "Sales", "Json");
        _map["CTR006"] = new("POST", "/api/contracts/{{contractId}}/send", "Sales");
        _map["CTR007"] = new("POST", "/api/contracts/{{contractId}}/upload-signed", "Customer", "Form");
        _map["CTR008"] = new("POST", "/api/contracts/{{contractId}}/review", "Sales", "Json");
        _map["CTR009"] = new("POST", "/api/contracts/{{contractId}}/approve", "Customer");
        _map["CTR010"] = new("POST", "/api/contracts/appendices/generate", "Admin", "Json");
        _map["CTR011"] = new("GET", "/api/contracts/appendices/preview?orderId={{orderId}}&adjustedPrice=1500000&reason=Demurrage", "Any");
        _map["CTR012"] = new("GET", "/api/contracts/appendices/{{appendixId}}", "Any");
        _map["CTR013"] = new("GET", "/api/contracts/appendices/by-order/{{orderId}}", "Any");
        _map["CTR014"] = new("POST", "/api/contracts/appendices/{{appendixId}}/send", "Sales");
        _map["CTR015"] = new("POST", "/api/contracts/appendices/{{appendixId}}/accept", "Customer");
        _map["CTR016"] = new("POST", "/api/contracts/appendices/{{appendixId}}/reject", "Customer");
        _map["CTR017"] = new("POST", "/api/contracts/appendices/{{appendixId}}/execute", "Admin");


        _map["ORD001"] = new("POST", "/api/orders", "Customer", "Form");
        _map["ORD002"] = new("PUT", "/api/orders/{{orderId}}", "Customer", "Form");
        _map["ORD003"] = new("PUT", "/api/orders/{{orderId}}/admin", "Admin", "Form");
        _map["ORD004"] = new("POST", "/api/orders/{{orderId}}/review", "Admin", "Json");
        _map["ORD006"] = new("GET", "/api/orders", "Admin");
        _map["ORD007"] = new("GET", "/api/orders/my-orders", "Customer");
        _map["ORD008"] = new("GET", "/api/orders/{{orderId}}", "Any");
        _map["ORD009"] = new("GET", "/api/orders/public-tracking/{{trackingToken}}", "Anonymous");
        _map["ORD010"] = new("GET", "/api/orders/public-tracking/{{trackingToken}}/temperature-chart", "Anonymous");
        _map["ORD011"] = new("POST", "/api/orders/{{orderId}}/physical-pod", "Driver", "Json");

        _map["INB001"] = new("POST", "/api/v1/asns", "Customer", "Json");
        _map["INB002"] = new("GET", "/api/v1/asns/schedule?date=2026-08-06&status=SCHEDULED", "Any");
        _map["INB003"] = new("GET", "/api/v1/asns", "Any");
        _map["INB004"] = new("GET", "/api/v1/asns/customer/{{customerId}}", "Any");
        _map["INB005"] = new("POST", "/api/inbound/qc", "Any", "Form");
        _map["INB006"] = new("PUT", "/api/inbound/qc/re-evaluate", "Any", "Form");
        _map["INB007"] = new("POST", "/api/inbound/receipts/generate", "Any", "Json");
        _map["INB008"] = new("POST", "/api/inbound/putaway", "Any", "Json");

        _map["OUT001"] = new("POST", "/api/outbound/orders", "Any", "Json");
        _map["OUT002"] = new("GET", "/api/outbound/orders", "Any");
        _map["OUT003"] = new("GET", "/api/outbound/orders/{{outboundOrderId}}", "Any");
        _map["OUT004"] = new("PUT", "/api/outbound/orders/{{outboundOrderId}}", "Any", "Json");
        _map["OUT005"] = new("POST", "/api/outbound/orders/{{outboundOrderId}}/allocate", "Any", "Json");
    }
}
