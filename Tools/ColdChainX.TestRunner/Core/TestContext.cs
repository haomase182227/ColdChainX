using System.Text.RegularExpressions;

namespace ColdChainX.TestRunner.Core;

public class TestContext
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _verbose;

    public TestContext(bool verbose = true)
    {
        _verbose = verbose;
        _variables["userId"] = "99991111-1111-1111-1111-111111119999"; // Dedicated test user for USR tests
        _variables["workerId"] = "22222222-2222-2222-2222-222222222222"; // WarehouseWorker seeded user
        _variables["customerId"] = "33333333-3333-3333-3333-333333333333"; // Customer seeded user
        _variables["driverId"] = "55555555-5555-5555-5555-555555555555"; // Driver seeded entity
        _variables["vehicleId"] = "77777777-7777-7777-7777-777777777777"; // Vehicle seeded entity
        _variables["orderId"] = "75000000-0000-0000-0000-000000000101"; // Seeded transport order
        _variables["asnId"] = "77000000-0000-0000-0000-000000000101"; // Seeded ASN
        _variables["receiptId"] = "76000000-0000-0000-0000-000000000101"; // Seeded warehouse receipt
        _variables["lpnId"] = "78000000-0000-0000-0000-000000000101"; // Seeded in stock LPN
        _variables["warehouseId"] = "73000000-0000-0000-0000-000000000001"; // Seeded warehouse
        _variables["routeId"] = "10000000-0000-0000-0000-000000000001"; // Seeded route
        _variables["scheduleId"] = "20000000-0000-0000-0000-000000000001"; // Seeded schedule
        _variables["stopId"] = "30000000-0000-0000-0000-000000000001"; // Seeded stop
        _variables["tierId"] = "87b07384-d113-46c6-950c-619f7e5b32cd"; // Seeded tier
        _variables["catalogId"] = "22222222-2222-2222-2222-222222222222"; // Seeded catalog (BOC_MANG_CO)
        _variables["quotationId"] = "90000000-0000-0000-0000-000000000001"; // Fallback quotation
        _variables["contractId"] = "91000000-0000-0000-0000-000000000001"; // Fallback contract
        _variables["appendixId"] = "92000000-0000-0000-0000-000000000001"; // Fallback appendix
        _variables["conversationId"] = "93000000-0000-0000-0000-000000000001"; // Fallback conversation
        _variables["tripId"] = "99000000-0000-0000-0000-000000000101"; // Fallback trip
        _variables["trackingCode"] = "CCX-2026-TRK-8891";
        _variables["trackingToken"] = "CCX-2026-TRK-8891";
        _variables["quoteId"] = "90000000-0000-0000-0000-000000000001";
        _variables["outboundOrderId"] = "94000000-0000-0000-0000-000000000001";
        _variables["outboundId"] = "94000000-0000-0000-0000-000000000001";
        _variables["deleteRouteId"] = "10000000-0000-0000-0000-000000000002";
        _variables["deleteScheduleId"] = "20000000-0000-0000-0000-000000000002";
        _variables["deleteStopId"] = "30000000-0000-0000-0000-000000000002";
        _variables["deleteTierId"] = "87b07384-d113-46c6-950c-619f7e5b32ce";
        _variables["adjustedPrice"] = "1500000";
        _variables["reason"] = "Demurrage overtime fee";
        _variables["deviceId"] = "88888888-8888-8888-8888-888888888888";
        _variables["deviceCode"] = "IOT-TRK-001";
        _variables["sealNumber"] = "SEAL-2026-001";
        _variables["stagingBayId"] = "BAY-01";
        _variables["claimId"] = "95000000-0000-0000-0000-000000000001";
        _variables["invoiceId"] = "96000000-0000-0000-0000-000000000001";
        _variables["paymentId"] = "97000000-0000-0000-0000-000000000001";
        _variables["incidentId"] = "98000000-0000-0000-0000-000000000001";
        _variables["ticketId"] = "89000000-0000-0000-0000-000000000001";
        _variables["assignmentId"] = "85000000-0000-0000-0000-000000000001";
        _variables["epodId"] = "86000000-0000-0000-0000-000000000001";
        _variables["notificationId"] = "84000000-0000-0000-0000-000000000001";
        _variables["discrepancyId"] = "83000000-0000-0000-0000-000000000001";
        _variables["accountantToken"] = "";
    }

    private TestContext(Dictionary<string, string> variables, bool verbose)
    {
        _verbose = verbose;
        _variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);
    }

    public TestContext Clone()
        => new(_variables, _verbose);

    public void MergeFrom(TestContext source)
    {
        foreach (var (key, value) in source.All)
        {
            if (!string.IsNullOrWhiteSpace(value))
                _variables[key] = value;
        }
    }

    public void Set(string key, string value)
    {
        _variables[key] = value;
        if (key == "quoteId") _variables["quotationId"] = value;
        if (key == "quotationId") _variables["quoteId"] = value;
        if (_verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"   💾 ctx.{key} = {Truncate(value, 60)}");
            Console.ResetColor();
        }
    }

    public string? Get(string key)
        => _variables.TryGetValue(key, out var v) ? v : null;

    public bool Has(string key)
        => _variables.ContainsKey(key);

    public void Remove(string key)
    {
        _variables.Remove(key);
    }

    public string Resolve(string template)
    {
        var result = template;
        foreach (var (key, value) in _variables)
            result = result.Replace($"{{{{{key}}}}}", value);

        result = Regex.Replace(result, @"\{\{[^}]+\}\}", "11111111-1111-1111-1111-111111111111");
        return result;
    }

    public string? GetToken(string role)
    {
        if (_variables.TryGetValue($"{role}Token", out var token) && !string.IsNullOrWhiteSpace(token))
            return token;
        if (_variables.TryGetValue($"{role.ToLower()}Token", out token) && !string.IsNullOrWhiteSpace(token))
            return token;

        if (string.Equals(role, "WarehouseOperator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "WarehouseManager", StringComparison.OrdinalIgnoreCase))
        {
            if (_variables.TryGetValue("warehouseworkertoken", out token) && !string.IsNullOrWhiteSpace(token))
                return token;
        }
        if (string.Equals(role, "Dispatcher", StringComparison.OrdinalIgnoreCase))
        {
            if (_variables.TryGetValue("dispatchertoken", out token) && !string.IsNullOrWhiteSpace(token))
                return token;
        }

        if (_variables.TryGetValue("adminToken", out token) && !string.IsNullOrWhiteSpace(token))
            return token;
        return null;
    }

    public void Dump()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n── Context Variables ──");
        foreach (var (key, value) in _variables.OrderBy(kv => kv.Key))
            Console.WriteLine($"  {key} = {Truncate(value, 80)}");
        Console.WriteLine("──────────────────────\n");
        Console.ResetColor();
    }

    public IReadOnlyDictionary<string, string> All => _variables;

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";
}
