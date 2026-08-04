using System.Text.Json;

namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Extracts important values (tokens, IDs) from API responses
/// and saves them to TestContext for chaining.
/// </summary>
public static class ResponseExtractor
{
    public static void Extract(TestContext ctx, string functionCode, string responseBody)
    {
        try
        {
            var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Check if response has "data" field
            if (!root.TryGetProperty("data", out var data))
                return;

            // Function-specific extraction
            switch (functionCode)
            {
                case "AUTH001": // CreateCustomer
                    SaveIfExists(ctx, data, "accessToken", "customerToken");
                    SaveIfExists(ctx, data, "refreshToken", "customerRefreshToken");
                    SaveIfExists(ctx, data, "customerId", "customerId");
                    SaveIfExists(ctx, data, "userId", "lastCreatedUserId");
                    break;

                case "AUTH002": // CreateDriver
                    SaveIfExists(ctx, data, "accessToken", "driverToken");
                    SaveIfExists(ctx, data, "userId", "driverId");
                    break;

                case "AUTH003": // CreateWarehouseWorker
                    SaveIfExists(ctx, data, "accessToken", "workerToken");
                    SaveIfExists(ctx, data, "userId", "workerId");
                    break;

                case "AUTH004": // Login
                    SaveIfExists(ctx, data, "accessToken", "lastLoginToken");
                    SaveIfExists(ctx, data, "refreshToken", "lastRefreshToken");
                    SaveIfExists(ctx, data, "userId", "userId");
                    SaveIfExists(ctx, data, "customerId", "customerId");
                    // Auto-detect role and save role-specific token
                    AutoSaveRoleToken(ctx, data);
                    break;

                default:
                    // Generic: auto-extract any *Id and *Token fields
                    AutoExtractIds(ctx, data, functionCode);
                    break;
            }
        }
        catch
        {
            // Silently ignore extraction errors
        }
    }

    private static void AutoSaveRoleToken(TestContext ctx, JsonElement data)
    {
        string? roleName = null;

        // Try to find role in nested objects
        if (data.TryGetProperty("roleName", out var rn))
            roleName = rn.GetString();
        else if (data.TryGetProperty("role", out var r))
        {
            if (r.ValueKind == JsonValueKind.String)
                roleName = r.GetString();
            else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("roleName", out var rrn))
                roleName = rrn.GetString();
        }

        if (!string.IsNullOrEmpty(roleName) && data.TryGetProperty("accessToken", out var tok))
        {
            var tokenVal = tok.GetString();
            if (!string.IsNullOrEmpty(tokenVal))
            {
                var key = roleName.ToLower().Replace(" ", "") + "Token";
                ctx.Set(key, tokenVal);
            }
        }
    }

    private static void AutoExtractIds(TestContext ctx, JsonElement data, string funcCode)
    {
        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in data.EnumerateObject())
            {
                var name = prop.Name;
                if ((name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith("Code", StringComparison.OrdinalIgnoreCase)) &&
                    prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    var val = prop.Value.ToString();
                    if (!string.IsNullOrEmpty(val) && val != "00000000-0000-0000-0000-000000000000")
                        ctx.Set(name, val);
                }
            }
        }
        // If data is an array and has items, extract from first item
        else if (data.ValueKind == JsonValueKind.Array)
        {
            var arr = data.EnumerateArray().ToList();
            if (arr.Count > 0 && arr[0].ValueKind == JsonValueKind.Object)
            {
                AutoExtractIds(ctx, arr[0], funcCode);
            }
        }
    }

    private static void SaveIfExists(TestContext ctx, JsonElement data, string jsonKey, string ctxKey)
    {
        if (data.TryGetProperty(jsonKey, out var val))
        {
            var strVal = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
            if (!string.IsNullOrEmpty(strVal) && strVal != "00000000-0000-0000-0000-000000000000")
                ctx.Set(ctxKey, strVal);
        }
    }
}
