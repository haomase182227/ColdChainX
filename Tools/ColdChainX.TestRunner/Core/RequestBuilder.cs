using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Builds HTTP requests from test case specifications.
/// Handles: URL path parameters, query params, JSON body, Form body, auth header.
/// </summary>
public static class RequestBuilder
{
    public static HttpRequestMessage Build(EndpointInfo endpoint, TestSpec spec, TestCaseSpec tc,
        TestContext ctx, string? authToken)
    {
        // 1. Resolve URL (replace {{variables}})
        var url = ctx.Resolve(endpoint.Url);

        // 2. Build request
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), url);

        // 3. Add auth header
        if (authToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        // 4. Build body based on function code and test case inputs
        if (endpoint.BodyType == "Json")
            request.Content = BuildJsonBody(spec, tc, ctx);
        else if (endpoint.BodyType == "Form")
            request.Content = BuildFormBody(spec, tc, ctx);

        return request;
    }

    private static StringContent BuildJsonBody(TestSpec spec, TestCaseSpec tc, TestContext ctx)
    {
        var body = new Dictionary<string, object>();

        foreach (var (inputKey, inputIndex) in tc.Inp)
        {
            if (!spec.Inputs.ContainsKey(inputKey)) continue;
            var inputValues = spec.Inputs[inputKey];
            if (inputIndex >= inputValues.Count) continue;
            var inputDesc = inputValues[inputIndex];

            // Parse input description to extract actual values
            var fields = ParseInputDescription(inputKey, inputDesc, spec, ctx);
            foreach (var (k, v) in fields)
                body[k] = v;
        }

        var json = JsonSerializer.Serialize(body);
        json = ctx.Resolve(json); // Replace any remaining {{variables}}
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static MultipartFormDataContent BuildFormBody(TestSpec spec, TestCaseSpec tc, TestContext ctx)
    {
        var form = new MultipartFormDataContent();

        foreach (var (inputKey, inputIndex) in tc.Inp)
        {
            if (!spec.Inputs.ContainsKey(inputKey)) continue;
            var inputValues = spec.Inputs[inputKey];
            if (inputIndex >= inputValues.Count) continue;
            var inputDesc = inputValues[inputIndex];

            var fields = ParseInputDescription(inputKey, inputDesc, spec, ctx);
            foreach (var (k, v) in fields)
                form.Add(new StringContent(ctx.Resolve(v.ToString() ?? "")), k);
        }

        return form;
    }

    /// <summary>
    /// Parse input description text into key-value pairs.
    /// Examples:
    ///   "Email=driver.tran@coldchain.vn, LicenseNumber='51B-12345', FullName='Tran Van B'"
    ///   "newcustomer@coldchain.vn (valid new)"
    ///   "ColdChain@2026 (strong valid password)"
    ///   "empty string ("")"
    ///   "Valid complete request payload with all required business fields"
    /// </summary>
    private static Dictionary<string, object> ParseInputDescription(string inputKey, string desc, TestSpec spec, TestContext ctx)
    {
        var result = new Dictionary<string, object>();

        // Strip annotation text in parentheses for actual value
        var cleanDesc = desc.Trim();

        // Handle "empty string" explicitly
        if (cleanDesc.StartsWith("empty string", StringComparison.OrdinalIgnoreCase))
        {
            var fieldName = ExtractFieldName(inputKey);
            result[fieldName] = "";
            return result;
        }

        // Handle key=value pairs: "Email=xxx, LicenseNumber='yyy', FullName='zzz'"
        var kvMatches = Regex.Matches(cleanDesc, @"(\w+)\s*=\s*(?:'([^']*)'|""([^""]*)""|(\S+))");
        if (kvMatches.Count > 0)
        {
            foreach (Match m in kvMatches)
            {
                var key = ToCamelCase(m.Groups[1].Value);
                var val = m.Groups[2].Success ? m.Groups[2].Value :
                          m.Groups[3].Success ? m.Groups[3].Value :
                          m.Groups[4].Value;
                // Strip trailing annotation like "(already used)"
                val = Regex.Replace(val, @"\s*\(.*?\)\s*$", "").Trim();
                // Resolve context variables
                result[key] = ctx.Resolve(val);
            }
            return result;
        }

        // Handle single value with field name derived from input key
        var fieldNameSingle = ExtractFieldName(inputKey);
        var actualValue = Regex.Replace(cleanDesc, @"\s*\(.*?\)\s*$", "").Trim();
        actualValue = ctx.Resolve(actualValue);

        // Special handling for specific known patterns
        if (cleanDesc.Contains("Non-existent ID", StringComparison.OrdinalIgnoreCase) ||
            cleanDesc.Contains("id=9999", StringComparison.OrdinalIgnoreCase))
        {
            result[fieldNameSingle] = "00000000-0000-0000-0000-000000009999";
            return result;
        }

        if (cleanDesc.Contains("Valid existing ID", StringComparison.OrdinalIgnoreCase) ||
            cleanDesc.Contains("Valid complete request", StringComparison.OrdinalIgnoreCase))
        {
            // Use context values if available, otherwise generate sample data
            result = BuildSamplePayload(spec, ctx);
            return result;
        }

        result[fieldNameSingle] = actualValue;
        return result;
    }

    /// <summary>Build a complete sample payload based on the function type</summary>
    private static Dictionary<string, object> BuildSamplePayload(TestSpec spec, TestContext ctx)
    {
        var result = new Dictionary<string, object>();

        switch (spec.Code)
        {
            case "AUTH001": // CreateCustomer
                result["email"] = $"test.cust.{Guid.NewGuid():N}"[..30] + "@coldchain.vn";
                result["password"] = "TestPass@2026";
                result["fullName"] = "Test Customer Auto";
                result["companyName"] = "Auto Test Company JSC";
                result["taxCode"] = $"TAX-{Random.Shared.Next(100000, 999999)}";
                break;
            case "AUTH002": // CreateDriver
                result["email"] = $"test.driver.{Guid.NewGuid():N}"[..30] + "@coldchain.vn";
                result["password"] = "TestPass@2026";
                result["fullName"] = "Test Driver Auto";
                result["licenseNumber"] = $"51B-{Random.Shared.Next(10000, 99999)}";
                result["dateOfBirth"] = "1990-01-15";
                break;
            case "AUTH003": // CreateWarehouseWorker
                result["email"] = $"test.worker.{Guid.NewGuid():N}"[..30] + "@coldchain.vn";
                result["password"] = "TestPass@2026";
                result["fullName"] = "Test Worker Auto";
                if (ctx.Has("warehouseId"))
                    result["warehouseId"] = ctx.Get("warehouseId")!;
                break;
            case "AUTH004": // Login
                result["email"] = "admin@coldchain.vn";
                result["password"] = "Admin@2026";
                break;
            default:
                // Generic: try to use context values for common fields
                if (ctx.Has("orderId")) result["orderId"] = ctx.Get("orderId")!;
                if (ctx.Has("customerId")) result["customerId"] = ctx.Get("customerId")!;
                if (ctx.Has("routeId")) result["routeId"] = ctx.Get("routeId")!;
                break;
        }

        return result;
    }

    private static string ExtractFieldName(string inputKey)
    {
        // "Input - Email" → "email"
        // "Input - DriverInfo" → "driverInfo"  
        // "Input - Query / ID Parameter" → "id"
        var name = inputKey;
        if (name.StartsWith("Input - ", StringComparison.OrdinalIgnoreCase))
            name = name[8..];
        if (name.Contains("Query") || name.Contains("Parameter"))
            name = "id";

        return ToCamelCase(name.Replace(" ", "").Replace("/", ""));
    }

    private static string ToCamelCase(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLower(s[0]) + s[1..];
}
