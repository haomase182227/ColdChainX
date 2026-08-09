using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

public static class RequestBuilder
{
    public static HttpRequestMessage Build(EndpointInfo endpoint, TestSpec spec, TestCaseSpec tc,
        TestContext ctx, string? authToken)
    {
        var rawUrl = endpoint.Url;

        var testCaseType = tc.Type?.Trim();
        if (spec.Code == "FLT003")
        {
            var deleteVehicleId = ctx.Get("deleteVehicleId");
            var shouldUseSeededDeleteVehicle =
                string.Equals(testCaseType, "N", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(testCaseType, "Normal", StringComparison.OrdinalIgnoreCase) ||
                tc.Desc.Contains("active business", StringComparison.OrdinalIgnoreCase) ||
                tc.Desc.Contains("active trip", StringComparison.OrdinalIgnoreCase);
            if (shouldUseSeededDeleteVehicle && !string.IsNullOrWhiteSpace(deleteVehicleId))
                rawUrl = rawUrl.Replace("{{vehicleId}}", deleteVehicleId);
        }

        if (tc.Type != "Normal" && tc.Type != "N")
        {
            if (spec.Code == "ACC010" && tc.Type != "B")
            {
                rawUrl = tc.Id == "UTCID02"
                    ? "/api/payments/transactions/customer/00000000-0000-0000-0000-000000009999"
                    : "/api/payments/transactions/customer/malformed-invalid-guid";
            }

            if (spec.Code == "RET002" && (tc.Id == "UTCID03" || tc.Id == "UTCID04"))
            {
                rawUrl = rawUrl.Replace("{{tripId}}", "00000000-0000-0000-0000-000000000000");
            }

            if (tc.Desc.Contains("non-existent", StringComparison.OrdinalIgnoreCase) ||
                tc.Desc.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                tc.Desc.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                tc.Desc.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                tc.Desc.Contains("corrupted", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{orderId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{trackingToken}}", "INVALID-TRK-9999")
                               .Replace("{{id}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{lpnId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{tripId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{quotationId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{contractId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{customerId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{warehouseId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{routeId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{tierId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{catalogId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{scheduleId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{stopId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{appendixId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{userId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{driverId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{vehicleId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{deviceId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{ticketId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{claimId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{invoiceId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{incidentId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{paymentId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{discrepancyId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{assignmentId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{configKey}}", "INVALID_NON_EXISTENT_KEY")
                               .Replace("{{epodId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{notificationId}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{receiptId}}", "00000000-0000-0000-0000-000000009999");
            }
            if (tc.Desc.Contains("empty", StringComparison.OrdinalIgnoreCase) &&
                !tc.Desc.Contains("empty list", StringComparison.OrdinalIgnoreCase) &&
                !tc.Desc.Contains("empty page", StringComparison.OrdinalIgnoreCase) &&
                !tc.Desc.Contains("empty result", StringComparison.OrdinalIgnoreCase) &&
                !tc.Desc.Contains("out of bounds", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{trackingToken}}", " ")
                               .Replace("{{orderId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{id}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{customerId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{lpnId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{tripId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{quotationId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{contractId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{warehouseId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{routeId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{tierId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{catalogId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{scheduleId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{stopId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{appendixId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{userId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{driverId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{vehicleId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{deviceId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{ticketId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{claimId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{invoiceId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{incidentId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{paymentId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{discrepancyId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{assignmentId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{configKey}}", "")
                               .Replace("{{epodId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{notificationId}}", "00000000-0000-0000-0000-000000000000")
                               .Replace("{{receiptId}}", "00000000-0000-0000-0000-000000000000");
            }
            if (tc.Desc.Contains("malformed", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{catalogId}}", "malformed-invalid-guid")
                               .Replace("{{id}}", "malformed-invalid-guid")
                               .Replace("{{routeId}}", "malformed-invalid-guid")
                               .Replace("{{stopId}}", "malformed-invalid-guid")
                               .Replace("{{scheduleId}}", "malformed-invalid-guid")
                               .Replace("{{deleteTierId}}", "malformed-invalid-guid")
                               .Replace("{{tierId}}", "malformed-invalid-guid")
                               .Replace("{{orderId}}", "malformed-invalid-guid")
                               .Replace("{{contractId}}", "malformed-invalid-guid")
                               .Replace("{{tripId}}", "malformed-invalid-guid")
                               .Replace("{{vehicleId}}", "malformed-invalid-guid")
                               .Replace("{{driverId}}", "malformed-invalid-guid")
                               .Replace("{{deviceId}}", "malformed-invalid-guid")
                               .Replace("{{claimId}}", "malformed-invalid-guid")
                               .Replace("{{invoiceId}}", "malformed-invalid-guid")
                               .Replace("{{incidentId}}", "malformed-invalid-guid")
                               .Replace("{{assignmentId}}", "malformed-invalid-guid")
                               .Replace("{{epodId}}", "malformed-invalid-guid")
                               .Replace("{{warehouseId}}", "malformed-invalid-guid");
            }
        }

        if (tc.Desc != null && (tc.Desc.Contains("their own account", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("self-lock", StringComparison.OrdinalIgnoreCase) || (spec.Code == "USR006" && tc.Id == "UTCID04") || (spec.Code == "USR009" && tc.Desc.Contains("own", StringComparison.OrdinalIgnoreCase))))
        {
            rawUrl = rawUrl.Replace("{{id}}", "11111111-1111-1111-1111-111111111111")
                           .Replace("{{userId}}", "11111111-1111-1111-1111-111111111111");
        }

        if (spec.Code == "USR007")
        {
            if (tc.Desc != null && tc.Desc.Contains("Customer role", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "33333333-3333-3333-3333-333333333333")
                               .Replace("{{userId}}", "33333333-3333-3333-3333-333333333333");
            }
            else if (tc.Desc != null && tc.Desc.Contains("non-existent user", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{userId}}", "00000000-0000-0000-0000-000000009999");
            }
            else
            {
                var workerId = ctx.Has("workerId") ? ctx.Get("workerId")! : "22222222-2222-2222-2222-222222222222";
                rawUrl = rawUrl.Replace("{{id}}", workerId)
                               .Replace("{{userId}}", workerId);
            }
        }
        if (spec.Code == "USR008")
        {
            if (tc.Desc != null && tc.Desc.Contains("soft-deleted", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "99992222-2222-2222-2222-222222229999")
                               .Replace("{{userId}}", "99992222-2222-2222-2222-222222229999");
            }
            else if (tc.Desc != null && tc.Desc.Contains("non-existent", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{userId}}", "00000000-0000-0000-0000-000000009999");
            }
            else
            {
                var activeUser = ctx.Has("workerId") ? ctx.Get("workerId")! : "22222222-2222-2222-2222-222222222222";
                rawUrl = rawUrl.Replace("{{id}}", activeUser)
                               .Replace("{{userId}}", activeUser);
            }
        }
        if (spec.Code == "USR009")
        {
            if (tc.Desc != null && (tc.Desc.Contains("own account", StringComparison.OrdinalIgnoreCase) || tc.Id == "UTCID04"))
            {
                rawUrl = rawUrl.Replace("{{id}}", "11111111-1111-1111-1111-111111111111")
                               .Replace("{{userId}}", "11111111-1111-1111-1111-111111111111");
            }
            else if (tc.Desc != null && tc.Desc.Contains("driver", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "44444444-4444-4444-4444-444444444444")
                               .Replace("{{userId}}", "44444444-4444-4444-4444-444444444444");
            }
            else if (tc.Desc != null && tc.Desc.Contains("already deleted", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "99992222-2222-2222-2222-222222229999")
                               .Replace("{{userId}}", "99992222-2222-2222-2222-222222229999");
            }
            else if (tc.Desc != null && tc.Desc.Contains("non-existent", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{userId}}", "00000000-0000-0000-0000-000000009999");
            }
            else
            {
                rawUrl = rawUrl.Replace("{{id}}", "99991111-1111-1111-1111-111111119999")
                               .Replace("{{userId}}", "99991111-1111-1111-1111-111111119999");
            }
        }
        if (spec.Code == "USR010")
        {
            if (tc.Desc != null && tc.Desc.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "00000000-0000-0000-0000-000000009999")
                               .Replace("{{userId}}", "00000000-0000-0000-0000-000000009999");
            }
            else if (tc.Desc != null && tc.Desc.Contains("active", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl.Replace("{{id}}", "11111111-1111-1111-1111-111111111111")
                               .Replace("{{userId}}", "11111111-1111-1111-1111-111111111111");
            }
            else
            {
                rawUrl = rawUrl.Replace("{{id}}", "99992222-2222-2222-2222-222222229999")
                               .Replace("{{userId}}", "99992222-2222-2222-2222-222222229999");
            }
        }

        var url = ctx.Resolve(rawUrl);
        url = AppendQueryString(url, spec, tc, ctx);

        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (authToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        if (endpoint.BodyType == "Json")
            request.Content = BuildJsonBody(spec, tc, ctx);
        else if (endpoint.BodyType == "Form")
            request.Content = BuildFormBody(spec, tc, ctx);

        return request;
    }

    private static string AppendQueryString(string url, TestSpec spec, TestCaseSpec tc, TestContext ctx)
    {
        var query = new List<string>();

        if (spec.Code == "DSP001")
        {
            var lpn = ctx.Get("lpnId") ?? "78000000-0000-0000-0000-000000000101";
            if (tc.Desc != null && (tc.Desc.Contains("no LPN", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("empty", StringComparison.OrdinalIgnoreCase)))
            {
            }
            else if (tc.Desc != null && tc.Desc.Contains("invalid LPN", StringComparison.OrdinalIgnoreCase))
            {
                query.Add("lpnIds=00000000-0000-0000-0000-000000009999");
            }
            else
            {
                query.Add($"lpnIds={lpn}");
            }
        }

        if (spec.Code == "RET002")
        {
            if (!url.Contains("tripId="))
            {
                var tripId = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                query.Add($"tripId={tripId}");
            }
        }

        if (spec.Code == "DEL014" && !url.Contains("customerId="))
        {
            var customerId = ctx.Get("customerId");
            if (!string.IsNullOrWhiteSpace(customerId))
                query.Add($"customerId={customerId}");
        }

        if (spec.Code == "CLM002" && tc.Type != "N" && tc.Type != "Normal")
        {
            if (tc.Id == "UTCID02")
                query.Add("pageSize=-5");
            else if (tc.Id == "UTCID03")
                query.Add("status=INVALID_STATUS");
        }

        if ((spec.Code == "ACC002" || spec.Code == "INC001" || spec.Code == "FLT005")
            && tc.Type != "N" && tc.Type != "Normal")
        {
            if (tc.Id == "UTCID02")
                query.Add("pageSize=-5");
            else if (tc.Id == "UTCID03")
                query.Add("pageNumber=0");
        }

        if ((spec.Code == "FLT008" || spec.Code == "DRV001" || spec.Code == "NTF001")
            && tc.Type != "N" && tc.Type != "Normal")
        {
            if (tc.Id == "UTCID02")
                query.Add("pageSize=-5");
            else if (tc.Id == "UTCID03")
                query.Add("pageNumber=0");
        }

        if ((spec.Code == "WH004" || spec.Code == "IOT001" || spec.Code == "ACC009" || spec.Code == "WRK001" || spec.Code == "RET005")
            && tc.Type != "N" && tc.Type != "Normal")
        {
            if (tc.Id == "UTCID02")
                query.Add("pageSize=-5");
            else if (tc.Id == "UTCID03")
                query.Add("pageNumber=0");
        }

        if ((spec.Code == "ACC006" || spec.Code == "ACC007" || spec.Code == "ACC008")
            && tc.Type != "N" && tc.Type != "Normal")
        {
            if (tc.Id == "UTCID02")
                query.Add("fromDate=2026-12-31&toDate=2026-01-01");
            else if (tc.Id == "UTCID03")
                query.Add("fromDate=invalid-date");
        }

        if (spec.Code == "ACC004" && tc.Type != "N" && tc.Type != "Normal")
        {
            if (tc.Id == "UTCID02")
                query.Add("periodStart=2026-12-31&periodEnd=2026-01-01");
            else if (tc.Id == "UTCID03")
                query.Add("periodStart=invalid-date");
        }

        if (spec.Code == "ACC005" && tc.Type != "N" && tc.Type != "Normal")
        {
            if (tc.Desc != null && tc.Desc.Contains("negative year", StringComparison.OrdinalIgnoreCase))
                query.Add("year=-1");
        }

        if (spec.Code == "MON001" && tc.Type != "N" && tc.Type != "Normal" && tc.Type != "B")
        {
            query.Add("statuses=INVALID_STATUS");
        }

        if (spec.Code == "MON003")
        {
            if (tc.Id == "UTCID01")
                query.Add("intervalMinutes=5");
            else if (tc.Id == "UTCID02")
                query.Add("intervalMinutes=1");
            else if (tc.Id == "UTCID04" || (tc.Desc != null && tc.Desc.Contains("negative sampling", StringComparison.OrdinalIgnoreCase)))
                query.Add("intervalMinutes=-5");
            else if (tc.Type == "B")
                query.Add("maxPoints=20");
        }

        if (spec.Code == "INV003")
        {
            if (tc.Id == "UTCID04" || (tc.Desc != null && tc.Desc.Contains("negative", StringComparison.OrdinalIgnoreCase)))
                query.Add("daysThreshold=-5");
            else if (tc.Id == "UTCID03" || (tc.Desc != null && tc.Desc.Contains("zero", StringComparison.OrdinalIgnoreCase)))
                query.Add("daysThreshold=0");
            else if (tc.Type == "N" || tc.Type == "Normal")
                query.Add("daysThreshold=30");
        }

        if (spec.Code == "USR001")
        {
            if (tc.Id == "UTCID01") query.Add("role=Driver");
            else if (tc.Id == "UTCID04" || (tc.Desc != null && tc.Desc.Contains("invalid role", StringComparison.OrdinalIgnoreCase)))
                query.Add("role=INVALID_ROLE");
        }

        if (tc.Desc != null)
        {
            if (tc.Desc.Contains("negative page size", StringComparison.OrdinalIgnoreCase))
                query.Add("pageSize=-5");
            else if (tc.Desc.Contains("out-of-range page index", StringComparison.OrdinalIgnoreCase))
                query.Add("page=99999");

            if (tc.Desc.Contains("invalid status", StringComparison.OrdinalIgnoreCase))
                query.Add(spec.Code == "MON001" ? "statuses=INVALID_STATUS" : "status=INVALID_STATUS");

            if (tc.Desc.Contains("inverted weight range", StringComparison.OrdinalIgnoreCase))
                query.Add("minWeight=1000&maxWeight=100");
        }

        if (query.Count == 0) return url;
        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + string.Join("&", query);
    }

    private static StringContent BuildJsonBody(TestSpec spec, TestCaseSpec tc, TestContext ctx)
    {
        if (spec.Code == "ORD004")
        {
            var action = "APPROVE";
            if (tc.Id == "UTCID02" || (tc.Desc != null && (tc.Desc.Contains("reject", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("COMPLIANCE_REJECT", StringComparison.OrdinalIgnoreCase))))
                action = "COMPLIANCE_REJECT";

            var bodyObj = new Dictionary<string, object>
            {
                ["Action"] = action,
                ["CustomerNote"] = action == "COMPLIANCE_REJECT" ? "Rejection due to refrigerated vehicle unavailability" : "Approved for quotation"
            };
            return new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");
        }

        var body = BuildSamplePayload(spec, ctx);

        foreach (var (inputKey, inputIndex) in tc.Inp)
        {
            if (!spec.Inputs.ContainsKey(inputKey)) continue;
            var inputValues = spec.Inputs[inputKey];
            if (inputIndex >= inputValues.Count) continue;
            var inputDesc = inputValues[inputIndex];

            var fields = ParseInputDescription(inputKey, inputDesc, spec, ctx);
            foreach (var (k, v) in fields)
                body[k] = v;
        }

        ApplyUniqueStateAdjustments(body, spec, tc, ctx);

        var json = JsonSerializer.Serialize(body);
        json = ctx.Resolve(json);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static MultipartFormDataContent BuildFormBody(TestSpec spec, TestCaseSpec tc, TestContext ctx)
    {
        var form = new MultipartFormDataContent();
        var body = BuildSamplePayload(spec, ctx);

        foreach (var (inputKey, inputIndex) in tc.Inp)
        {
            if (!spec.Inputs.ContainsKey(inputKey)) continue;
            var inputValues = spec.Inputs[inputKey];
            if (inputIndex >= inputValues.Count) continue;
            var inputDesc = inputValues[inputIndex];

            var fields = ParseInputDescription(inputKey, inputDesc, spec, ctx);
            foreach (var (k, v) in fields)
                body[k] = v;
        }

        ApplyUniqueStateAdjustments(body, spec, tc, ctx);

        foreach (var (k, v) in body)
        {
            if (v is List<string> listStr)
            {
                foreach (var item in listStr)
                {
                    form.Add(new StringContent(ctx.Resolve(item)), k);
                }
            }
            else if (v != null)
            {
                form.Add(new StringContent(ctx.Resolve(v.ToString() ?? "")), k);
            }
        }

        AttachFilesForForm(spec, tc, form, ctx);

        return form;
    }

    private static void AttachFilesForForm(TestSpec spec, TestCaseSpec tc, MultipartFormDataContent form, TestContext ctx)
    {
        var sampleImageBytes = ReadSampleFileBytes("*.png", "*.jpg", "*.jpeg")
            ?? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        var samplePdfBytes = ReadSampleFileBytes("*.pdf")
            ?? Encoding.UTF8.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF");

        if (spec.Code == "WTR007")
        {
            byte[] csvBytes;
            if (tc.Id == "UTCID04")
            {
                csvBytes = new byte[0];
            }
            else if (tc.Id == "UTCID02")
            {
                csvBytes = Encoding.UTF8.GetBytes("RouteCode,MinWeightKg,MaxWeightKg,PricePerKg\nROUTE-INVALID-999,0,50,15000\n");
            }
            else if (tc.Id == "UTCID03")
            {
                csvBytes = Encoding.UTF8.GetBytes("RouteCode,MinWeightKg,MaxWeightKg,PricePerKg\nHCM-HN-TEST,50000,100000,25000\n");
            }
            else
            {
                var rCode = ctx.Get("routeCode") ?? "HCM-HN-TEST";
                var sb = new StringBuilder("RouteCode,MinWeightKg,MaxWeightKg,PricePerKg\n");
                long offset = DateTime.UtcNow.Ticks % 10000000;
                for (int i = 0; i < 15; i++)
                {
                    sb.Append($"{rCode},{offset + (i * 100)},{offset + (i * 100) + 99},15000\n");
                }
                csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
            }
            var csvContent = new ByteArrayContent(csvBytes);
            csvContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(csvContent, "file", "weight_tiers.csv");
        }
        else if (spec.Code == "CTR007")
        {
            var pdfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample.pdf");
            var pdfBytes = File.Exists(pdfPath) ? File.ReadAllBytes(pdfPath) : File.ReadAllBytes(@"C:\Users\ASUS\Music\CN 9\ĐA\ColdChainX\Tools\ColdChainX.TestRunner\sample.pdf");
            var pdfContent = new ByteArrayContent(pdfBytes);
            pdfContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(pdfContent, "SignedFile", "signed_contract.pdf");
        }
        else if (spec.Code == "DEL002")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "ProofImageFile", "checkin_proof.jpg");
        }
        else if (spec.Code == "DEL005")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "SignatureFile", "epod_signature.jpg");
        }
        else if (spec.Code == "DEL008")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "EvidenceImageFile", "cod_evidence.jpg");
        }
        else if (spec.Code == "DEL011")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "EvidenceImageFile", "noshow_evidence.jpg");
        }
        else if (spec.Code == "RET001")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "EvidenceImageFile", "damage_photo.jpg");
        }
        else if (spec.Code == "DEL009")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "PaymentEvidenceFile", "payment_evidence.jpg");
        }
        else if (spec.Code == "CLM001")
        {
            var imgContent = new ByteArrayContent(samplePdfBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(imgContent, "EvidenceImages", "claim_evidence.pdf");
        }
        else if (spec.Code == "INC003" || spec.Code == "INC004")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "EvidenceFiles", "incident_evidence.jpg");
            var singleContent = new ByteArrayContent(sampleImageBytes);
            singleContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(singleContent, "EvidenceFile", "incident_evidence.jpg");
            var filesContent = new ByteArrayContent(sampleImageBytes);
            filesContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(filesContent, "Files", "incident_evidence.jpg");
        }
        else if (spec.Code == "INC006")
        {
            var imgContent = new ByteArrayContent(sampleImageBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "ReceiptFile", "reimbursement_receipt.jpg");
        }
    }

    private static byte[]? ReadSampleFileBytes(params string[] patterns)
    {
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ColdChainX.API", "wwwroot", "uploads")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ColdChainX.API", "wwwroot"))
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var pattern in patterns)
            {
                var file = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(path => new FileInfo(path).Length > 0)
                    .OrderBy(path => new FileInfo(path).Length)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(file))
                    return File.ReadAllBytes(file);
            }
        }

        return null;
    }

    private static void ApplyUniqueStateAdjustments(Dictionary<string, object> body, TestSpec spec, TestCaseSpec tc, TestContext ctx)
    {
        bool isDuplicateTest = tc.Desc != null && (tc.Desc.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                                                   tc.Desc.Contains("existing", StringComparison.OrdinalIgnoreCase) ||
                                                   tc.Desc.Contains("already in use", StringComparison.OrdinalIgnoreCase));
        bool isInvalidTest = tc.Type == "A" || tc.Type == "E" || (tc.Desc != null && (
                             tc.Desc.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                             tc.Desc.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
                             tc.Desc.Contains("short", StringComparison.OrdinalIgnoreCase) ||
                             tc.Desc.Contains("missing", StringComparison.OrdinalIgnoreCase)));

        if (tc != null && tc.Desc != null)
        {
            if (tc.Desc.Contains("overlap", StringComparison.OrdinalIgnoreCase))
            {
                body["minWeightKg"] = 500m;
                body["maxWeightKg"] = 2000m;
            }
            if (tc.Desc.Contains("greater", StringComparison.OrdinalIgnoreCase))
            {
                body["minWeightKg"] = 5000m;
                body["maxWeightKg"] = 1000m;
            }
            if (tc.Desc.Contains("negative", StringComparison.OrdinalIgnoreCase))
            {
                body["pricePerKg"] = -5000m;
                body["payloadCapacityKg"] = -1000m;
                body["volumeCapacityM3"] = -5m;
                body["estimatedCost"] = -500000m;
            }
            if (spec.Code == "WH001" || spec.Code == "WH002")
            {
                if (tc.Desc.Contains("duplicate warehouse code", StringComparison.OrdinalIgnoreCase))
                    body["warehouseCode"] = ctx.Get("warehouseCode") ?? "WH-001";
                if (tc.Desc.Contains("empty warehouse code", StringComparison.OrdinalIgnoreCase))
                    body["warehouseCode"] = "";
                if (tc.Desc.Contains("negative dock", StringComparison.OrdinalIgnoreCase))
                    body["maxPallets"] = -1;
                if (tc.Desc.Contains("empty thermal zones", StringComparison.OrdinalIgnoreCase))
                    body["defaultMinTemp"] = 8m;
            }
            if (spec.Code == "IOT003")
            {
                if (tc.Desc.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                    body["deviceCode"] = ctx.Get("deviceCode") ?? "IOT-TRK-001";
                if (tc.Desc.Contains("empty code", StringComparison.OrdinalIgnoreCase))
                    body["deviceCode"] = "";
                if (tc.Desc.Contains("negative sampling", StringComparison.OrdinalIgnoreCase))
                    body["samplingRateSeconds"] = -30;
            }
            if (spec.Code == "DRV003")
            {
                if (tc.Desc.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                    body["identityNumber"] = ctx.Get("driverIdentityNumber") ?? "123456789012";
                if (tc.Desc.Contains("expired", StringComparison.OrdinalIgnoreCase) && body.TryGetValue("license", out var licenseObj) && licenseObj is Dictionary<string, object> license)
                    license["expiryDate"] = "2020-01-01";
                if (tc.Desc.Contains("empty name", StringComparison.OrdinalIgnoreCase))
                {
                    body["fullName"] = "";
                    body["phoneNumber"] = "";
                }
            }
            if (spec.Code == "CTR008")
            {
                body["action"] = "REQUEST_RESUBMIT";
                body["customerNote"] = "Reviewed and resubmission requested for signature clarification";
            }
            if (spec.Code.StartsWith("QOT"))
            {
                if (tc.Desc.Contains("non-existent route", StringComparison.OrdinalIgnoreCase))
                {
                    body["routeId"] = "00000000-0000-0000-0000-000000009999";
                }
                if (tc.Desc.Contains("negative cargo weight", StringComparison.OrdinalIgnoreCase))
                {
                    body["weightKg"] = -100m;
                }
                if (tc.Desc.Contains("unrecognized temperature", StringComparison.OrdinalIgnoreCase))
                {
                    body["temperatureClass"] = "UNKNOWN";
                }
                if (tc.Desc.Contains("greater than 100%", StringComparison.OrdinalIgnoreCase) || (tc.Desc.Contains("discount", StringComparison.OrdinalIgnoreCase) && tc.Type != "N"))
                {
                    body["discount"] = 150m;
                    body["discountPercentage"] = 150m;
                }
                if (tc.Desc.Contains("past ValidUntil", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("past", StringComparison.OrdinalIgnoreCase))
                {
                    body["validUntil"] = "2020-01-01T00:00:00Z";
                }
                if (tc.Desc.Contains("negative base price", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("negative base", StringComparison.OrdinalIgnoreCase))
                {
                    body["baseFreight"] = -100m;
                }
            }
            if (spec.Code == "ROU003" || spec.Code == "ROU004")
            {
                if (tc.Desc.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                    body["routeCode"] = "HCM-HN-TEST";
                if (tc.Desc.Contains("negative distance", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("identical", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("same Origin", StringComparison.OrdinalIgnoreCase))
                {
                    body["routeCode"] = $"INV-NEG-{Guid.NewGuid():N}"[..12];
                    body["distanceKm"] = -50m;
                    if (tc.Desc.Contains("identical", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("same Origin", StringComparison.OrdinalIgnoreCase))
                    {
                        body["originCity"] = "Ho Chi Minh City";
                        body["destCity"] = "Ho Chi Minh City";
                    }
                }
            }
            if (spec.Code == "ROU007" || spec.Code == "ROU008")
            {
                if (tc.Desc.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                    body["sequence"] = 1;
                if (tc.Desc.Contains("duration", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("invalid", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("sequence", StringComparison.OrdinalIgnoreCase))
                {
                    body["durationMinutes"] = -10;
                    body["sequence"] = -5;
                }
            }
            if (spec.Code == "ROU009" || spec.Code == "ROU010" || spec.Code == "ROU012" || spec.Code == "ROU013")
            {
                if (isInvalidTest && (tc.Desc.Contains("capacity", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("cutoff", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("invalid", StringComparison.OrdinalIgnoreCase)))
                {
                    body["capacity"] = -50;
                    body["departureTime"] = "06:00:00";
                    body["cutOffTime"] = "08:00:00";
                }
            }
        }

        if (spec.Code == "DSP001")
        {
            if (tc.Desc != null && tc.Desc.Contains("start >= end", StringComparison.OrdinalIgnoreCase))
            {
                body["PlannedStartTime"] = DateTime.UtcNow.AddHours(5).ToString("o");
                body["PlannedEndTime"] = DateTime.UtcNow.AddHours(2).ToString("o");
            }
            if (tc.Desc != null && tc.Desc.Contains("non-existent schedule", StringComparison.OrdinalIgnoreCase))
            {
                body["ScheduleId"] = "00000000-0000-0000-0000-000000009999";
            }
            if (tc.Desc != null && tc.Desc.Contains("no driver", StringComparison.OrdinalIgnoreCase))
            {
                body["DriverIds"] = new List<string>();
            }
        }

        if (spec.Code == "FLT001" || spec.Code == "FLT002")
        {
            if (isDuplicateTest)
            {
                body["truckPlate"] = "51C-99999";
            }
            else if (tc.Desc != null && tc.Desc.Contains("empty license plate", StringComparison.OrdinalIgnoreCase))
            {
                body["truckPlate"] = "";
            }
            else if (tc.Desc != null && tc.Desc.Contains("negative payload", StringComparison.OrdinalIgnoreCase))
            {
                body["maxWeight"] = -1000m;
            }
            else if (!isInvalidTest)
            {
                body["truckPlate"] = $"{Random.Shared.Next(50, 99)}C-{Random.Shared.Next(10000, 99999)}";
            }
        }

        if (spec.Code == "FLT007")
        {
            if (tc.Desc != null && (tc.Desc.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
                                    tc.Desc.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
                                    tc.Desc.Contains("negative", StringComparison.OrdinalIgnoreCase)))
            {
                body["cost"] = -1m;
            }
        }

        if (spec.Code == "IOT003" || spec.Code == "IOT004")
        {
            if (isDuplicateTest)
            {
                body["deviceCode"] = "IOT-TRK-001";
            }
            else if (!isInvalidTest)
            {
                body["deviceCode"] = $"IOT-SIM-{Guid.NewGuid():N}"[..12].ToUpper();
            }
        }

        if (spec.Code == "MON007")
        {
            if (tc.Id == "UTCID04" || (tc.Desc != null && tc.Desc.Contains("already actively paired", StringComparison.OrdinalIgnoreCase)))
            {
                body["deviceCode"] = ctx.Get("deviceCode") ?? "IOT-TRK-001";
                body["vehicleId"] = "77777777-7777-7777-7777-777777777777";
            }
            else if (tc.Id == "UTCID05" || (tc.Desc != null && tc.Desc.Contains("ACTIVE status", StringComparison.OrdinalIgnoreCase)))
            {
                body["deviceCode"] = ctx.Get("inactiveDeviceCode") ?? "IOT-INACTIVE-TEST";
            }
        }

        if (spec.Code == "USR006")
        {
            if (tc.Id == "UTCID02" || (tc.Desc != null && tc.Desc.Contains("reactivate", StringComparison.OrdinalIgnoreCase)))
                body["status"] = 0;
            else
                body["status"] = 1;
        }

        if (spec.Code == "USR007" && tc.Desc != null && tc.Desc.Contains("non-existent warehouse", StringComparison.OrdinalIgnoreCase))
        {
            body["warehouseId"] = "00000000-0000-0000-0000-000000009999";
        }

        if (spec.Code == "USR008" && tc.Desc != null && (tc.Desc.Contains("short", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("< 6", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("< 8", StringComparison.OrdinalIgnoreCase)))
        {
            body["newPassword"] = "123";
        }

        if (spec.Code == "AUTH004")
        {
            if (tc.Desc != null && (tc.Desc.Contains("case-insensitive", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("ADMIN@", StringComparison.OrdinalIgnoreCase)))
            {
                body["email"] = "ADMIN01@COLDCHAINX.COM";
                body["password"] = "Password@123";
            }
            else if (tc.Desc != null && (tc.Desc.Contains("incorrect", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("wrong", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("invalid password", StringComparison.OrdinalIgnoreCase)))
            {
                body["email"] = "admin01@coldchainx.com";
                body["password"] = "WrongPass@2026";
            }
            else if (tc.Desc != null && (tc.Desc.Contains("unregistered", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("non-existent", StringComparison.OrdinalIgnoreCase)))
            {
                body["email"] = $"nonexistent_{Guid.NewGuid():N}"[..20] + "@notexist.com";
                body["password"] = "Password@123";
            }
            else if (tc.Desc != null && (tc.Desc.Contains("locked", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("inactive", StringComparison.OrdinalIgnoreCase)))
            {
                body["email"] = $"locked_user_{Guid.NewGuid():N}"[..20] + "@coldchainx.com";
                body["password"] = "Password@123";
            }
            else if (tc.Desc != null && tc.Desc.Contains("empty password", StringComparison.OrdinalIgnoreCase))
            {
                body["email"] = "admin01@coldchainx.com";
                body["password"] = "";
            }
            else
            {
                body["email"] = "admin01@coldchainx.com";
                body["password"] = "Password@123";
            }
        }

        if (spec.Code == "AUTH006")
        {
            if (tc.Id == "UTCID03" || (tc.Desc != null && tc.Desc.Contains("incorrect", StringComparison.OrdinalIgnoreCase)))
            {
                body["currentPassword"] = "WrongPass@123456";
                body["newPassword"] = "NewPass@2026!";
            }
            else if (tc.Id == "UTCID04" || (tc.Desc != null && (tc.Desc.Contains("short", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("< 6", StringComparison.OrdinalIgnoreCase))))
            {
                body["currentPassword"] = "Password@123";
                body["newPassword"] = "123";
            }
            else if (tc.Id == "UTCID05" || (tc.Desc != null && (tc.Desc.Contains("identical", StringComparison.OrdinalIgnoreCase) || tc.Desc.Contains("same", StringComparison.OrdinalIgnoreCase))))
            {
                body["currentPassword"] = "Password@123";
                body["newPassword"] = "Password@123";
            }
            else
            {
                body["currentPassword"] = "Password@123";
                body["newPassword"] = "NewPass@2026!";
            }
        }

        if (spec.Code == "WRK003" && tc.Desc != null)
        {
            if (tc.Desc.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                tc.Desc.Contains("Citizen ID", StringComparison.OrdinalIgnoreCase))
            {
                body["username"] = "warehouseworker01";
                body["email"] = "warehouseworker01@coldchainx.com";
            }
            if (tc.Desc.Contains("non-existent warehouse", StringComparison.OrdinalIgnoreCase))
            {
                body["warehouseId"] = "00000000-0000-0000-0000-000000009999";
            }
            if (tc.Desc.Contains("empty name", StringComparison.OrdinalIgnoreCase))
            {
                body["fullName"] = "";
                body["phone"] = "";
            }
        }

        if (spec.Code == "CLM001" && tc.Desc != null)
        {
            if (tc.Desc.Contains("non-existent order", StringComparison.OrdinalIgnoreCase))
                body["OrderId"] = "00000000-0000-0000-0000-000000009999";
            if (tc.Desc.Contains("empty description", StringComparison.OrdinalIgnoreCase))
                body["Description"] = "";
        }
    }

    private static Dictionary<string, object> ParseInputDescription(string inputKey, string desc, TestSpec spec, TestContext ctx)
    {
        var result = new Dictionary<string, object>();
        var cleanDesc = desc.Trim();

        if (cleanDesc.StartsWith("empty string", StringComparison.OrdinalIgnoreCase))
        {
            var fieldName = InferFieldName(inputKey, cleanDesc, spec);
            result[fieldName] = "";
            return result;
        }

        var lines = cleanDesc.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        bool foundStructured = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('-', '*', '•').Trim();
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx > 0 && colonIdx < trimmed.Length - 1)
            {
                var key = trimmed[..colonIdx].Trim();
                var val = trimmed[(colonIdx + 1)..].Trim().Trim('"', '\'');
                if (key.Length < 40 && !key.Contains(' '))
                {
                    result[ToCamelCase(key)] = ParseValue(val, ctx);
                    foundStructured = true;
                }
            }
        }

        if (!foundStructured)
        {
            var fieldName = InferFieldName(inputKey, cleanDesc, spec);
            result[fieldName] = ParseValue(cleanDesc, ctx);
        }

        return result;
    }

    private static object ParseValue(string val, TestContext ctx)
    {
        val = ctx.Resolve(val);

        if (val.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (val.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (val.Equals("null", StringComparison.OrdinalIgnoreCase)) return null!;

        if (int.TryParse(val, out var intVal)) return intVal;
        if (decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var decVal))
            return decVal;

        return val;
    }

    private static string InferFieldName(string inputKey, string desc, TestSpec spec)
    {
        var keyLower = inputKey.ToLower().Trim();

        if (keyLower.Contains("name") || desc.Contains("Name")) return "name";
        if (keyLower.Contains("email") || desc.Contains("@")) return "email";
        if (keyLower.Contains("password") || keyLower.Contains("pass")) return "password";
        if (keyLower.Contains("phone")) return "phone";
        if (keyLower.Contains("address")) return "address";
        if (keyLower.Contains("route")) return "routeCode";
        if (keyLower.Contains("weight")) return "weightKg";
        if (keyLower.Contains("temp")) return "temperature";
        if (keyLower.Contains("price")) return "price";
        if (keyLower.Contains("quantity") || keyLower.Contains("qty")) return "quantity";
        if (keyLower.Contains("status")) return "status";
        if (keyLower.Contains("reason")) return "reason";
        if (keyLower.Contains("note")) return "notes";
        if (keyLower.Contains("plate")) return "truckPlate";
        if (keyLower.Contains("code")) return "code";
        if (keyLower.Contains("seal")) return "sealNumber";

        return ToCamelCase(inputKey.Replace(" ", "").Replace("/", ""));
    }

    private static Dictionary<string, object> BuildSamplePayload(TestSpec spec, TestContext ctx)
    {
        var result = new Dictionary<string, object>();

        switch (spec.Code)
        {
            case "AUTH001":
                result["email"] = $"test.cust.{Guid.NewGuid():N}"[..26] + "@coldchain.vn";
                result["username"] = result["email"];
                result["password"] = "TestPass@2026";
                result["fullName"] = "Le Huy Vu Customer";
                result["companyName"] = "ColdChain Logistics JSC";
                result["taxCode"] = $"03{Random.Shared.Next(10000000, 99999999)}";
                break;
            case "AUTH002":
                result["email"] = $"test.driver.{Guid.NewGuid():N}"[..26] + "@coldchain.vn";
                result["username"] = result["email"];
                result["password"] = "TestPass@2026";
                result["fullName"] = "Le Huy Vu Driver";
                result["licenseNumber"] = $"{Random.Shared.Next(100000000, 999999999):D12}";
                result["licenseClass"] = "FC";
                result["dateOfBirth"] = "1990-01-15";
                result["issueDate"] = "2020-01-01";
                result["expiryDate"] = "2030-01-01";
                break;
            case "AUTH003":
                result["email"] = $"test.worker.{Guid.NewGuid():N}"[..26] + "@coldchain.vn";
                result["username"] = result["email"];
                result["password"] = "TestPass@2026";
                result["fullName"] = "Le Huy Vu Worker";
                result["warehouseId"] = ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001";
                break;
            case "AUTH004":
                result["email"] = "admin01@coldchainx.com";
                result["password"] = "Password@123";
                break;
            case "AUTH006":
                result["currentPassword"] = "Password@123";
                result["newPassword"] = "NewPass@2026!";
                break;
            case "USR003":
                result["email"] = $"staff.{Guid.NewGuid():N}"[..20] + "@coldchain.vn";
                result["username"] = result["email"];
                result["password"] = "Admin@2026";
                result["fullName"] = "Sales Staff Member";
                result["role"] = "Sales";
                result["status"] = "ACTIVE";
                break;
            case "USR004":
                result["email"] = $"updated.{Guid.NewGuid():N}"[..20] + "@coldchain.vn";
                result["fullName"] = "Updated User Profile";
                result["phone"] = "0988777666";
                result["address"] = "123 Logistics Way, HCMC";
                break;
            case "USR005":
                result["role"] = "Dispatcher";
                break;
            case "USR006":
                result["status"] = 1;
                result["reason"] = "Compliance lock for policy violation";
                break;
            case "USR007":
                result["warehouseId"] = ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001";
                break;
            case "USR008":
                result["newPassword"] = "NewPass@2026";
                break;

            case "ROU003":
            case "ROU004":
                result["routeCode"] = $"R-{Guid.NewGuid():N}"[..10];
                result["originCity"] = "Ho Chi Minh City";
                result["destCity"] = "Da Nang";
                result["transitTime"] = "36 hours";
                result["status"] = "Active";
                break;
            case "ROU007":
            case "ROU008":
                result["stopName"] = "Kho Trung Chuyen";
                result["durationMinutes"] = 30;
                result["sequence"] = 2;
                break;
            case "ROU009":
            case "ROU010":
            case "ROU012":
            case "ROU013":
                result["departureDate"] = DateTime.Now.AddDays(7).ToString("yyyy-MM-dd");
                result["departureTime"] = "08:00:00";
                result["cutOffTime"] = "06:00:00";
                result["status"] = "ACTIVE";
                break;
            case "WTR003":
            case "WTR004":
            case "WTR005":
                result["routeId"] = ctx.Get("routeId") ?? "10000000-0000-0000-0000-000000000001";
                result["minWeightKg"] = 100m;
                result["maxWeightKg"] = 500m;
                result["pricePerKg"] = 25000m;
                break;
            case "CAT003":
            case "CAT004":
            case "CAT005":
                result["serviceCode"] = $"SVC-{Guid.NewGuid():N}"[..10];
                result["serviceName"] = "Refrigerated Packaging 2-8C";
                result["description"] = "Standard cold packaging";
                result["defaultPrice"] = 150000m;
                result["isActive"] = true;
                result["isMandatory"] = false;
                break;

            case "ORD001":
            case "ORD002":
            case "ORD003":
                result["Item_Name"] = "Vaccine Cold Box 2-8C";
                result["itemName"] = "Vaccine Cold Box 2-8C";
                result["Category"] = "PHARMACEUTICALS";
                result["category"] = "PHARMACEUTICALS";
                result["Temp_Condition"] = -15.0m;
                result["tempCondition"] = -15.0m;
                result["Expected_Weight_KG"] = 3500m;
                result["expectedWeightKg"] = 3500m;
                result["Quantity"] = 4;
                result["quantity"] = 4;
                result["Packaging_Type"] = "Pallet";
                result["packagingType"] = "Pallet";
                result["Length_CM"] = 120m;
                result["lengthCm"] = 120m;
                result["Width_CM"] = 100m;
                result["widthCm"] = 100m;
                result["Height_CM"] = 150m;
                result["heightCm"] = 150m;
                result["Dest_Address_Text"] = "123 Le Loi, District 1, HCMC";
                result["destAddressText"] = "123 Le Loi, District 1, HCMC";
                result["Schedule_ID"] = ctx.Get("scheduleId") ?? "20000000-0000-0000-0000-000000000001";
                result["scheduleId"] = ctx.Get("scheduleId") ?? "20000000-0000-0000-0000-000000000001";
                result["Dropoff_Stop_ID"] = ctx.Get("dropoffStopId") ?? "30000000-0000-0000-0000-000000000001";
                result["dropoffStopId"] = ctx.Get("dropoffStopId") ?? "30000000-0000-0000-0000-000000000001";
                result["Has_Strong_Odor"] = false;
                result["Is_Stackable"] = true;
                break;
            case "CTR001":
            case "CTR005":
                result["orderId"] = ctx.Get("orderId") ?? "75000000-0000-0000-0000-000000000101";
                result["editedHtmlContent"] = "<div><p>Standard cold chain terms and SLA commitments.</p></div>";
                break;
            case "CTR008":
                result["action"] = "APPROVE";
                result["customerNote"] = "Approved signed contract";
                break;
            case "CTR010":
            case "CTR011":
                result["orderId"] = ctx.Get("orderId") ?? "75000000-0000-0000-0000-000000000301";
                result["adjustedPrice"] = 1500000m;
                result["reason"] = "QC discrepancy > 5% detected during inbound inspection";
                break;
            case "CHT004":
                result["content"] = "Hello, checking order status!";
                result["messageContent"] = "Hello, checking order status!";
                result["messageType"] = "TEXT";
                result["receiverId"] = ctx.Get("customerUserId") ?? ctx.Get("customerId") ?? "33333333-3333-3333-3333-333333333333";
                break;

            case "WH001":
            case "WH002":
                result["warehouseCode"] = $"WH-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
                result["warehouseName"] = $"Warehouse Alpha {Guid.NewGuid():N}"[..22];
                result["warehouseType"] = "COLD";
                result["address"] = "Khu Cong Nghiep Tan Binh, HCMC";
                result["maxPallets"] = 5000;
                result["defaultMinTemp"] = -20m;
                result["defaultMaxTemp"] = -15m;
                result["status"] = "ACTIVE";
                break;

            case "DSP001":
                result["ScheduleId"] = ctx.Get("scheduleId") ?? "20000000-0000-0000-0000-000000000001";
                result["VehicleId"] = ctx.Get("vehicleId") ?? "77777777-7777-7777-7777-777777777777";
                result["DriverIds"] = new List<string> { ctx.Get("driverId") ?? "55555555-5555-5555-5555-555555555555" };
                result["PlannedStartTime"] = DateTime.UtcNow.AddHours(2).ToString("o");
                result["PlannedEndTime"] = DateTime.UtcNow.AddHours(6).ToString("o");
                result["ScreenshotBase64"] = "";
                break;
            case "DSP004":
                result["SealCode"] = ctx.Get("sealNumber") ?? "SEAL-2026-001";
                result["VehicleId"] = ctx.Get("vehicleId") ?? "77777777-7777-7777-7777-777777777777";
                result["DriverIds"] = new List<string> { ctx.Get("driverId") ?? "55555555-5555-5555-5555-555555555555" };
                break;
            case "DSP005":
                result["tripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["reason"] = "Customer cancellation due to emergency change";
                break;

            case "MON007":
                result["vehicleId"] = ctx.Get("vehicleId") ?? "77777777-7777-7777-7777-777777777777";
                result["deviceCode"] = ctx.Get("deviceCode") ?? "IOT-TRK-001";
                result["note"] = "Assigned for refrigerated transport route";
                break;
            case "IOT003":
            case "IOT004":
                result["deviceCode"] = $"IOT-DEV-{Guid.NewGuid():N}"[..12].ToUpper();
                result["deviceType"] = "GPS_TEMPERATURE";
                result["vehicleId"] = ctx.Get("vehicleId") ?? "77777777-7777-7777-7777-777777777777";
                result["status"] = "ACTIVE";
                result["firmwareVersion"] = "v2.1.0";
                result["batteryLevel"] = 98;
                break;

            case "DEL001":
                result["tripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["warehouseId"] = ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001";
                break;
            case "DEL002":
                result["StopId"] = ctx.Get("stopId") ?? "30000000-0000-0000-0000-000000000001";
                result["Latitude"] = "10.762622";
                result["Longitude"] = "106.660172";
                result["OdometerKm"] = "15420";
                break;
            case "DEL003":
                result["tripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["stopId"] = ctx.Get("stopId") ?? "30000000-0000-0000-0000-000000000001";
                break;
            case "DEL004":
                result["tripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["sealCode"] = $"SEAL-NEW-{Guid.NewGuid():N}"[..12].ToUpper();
                break;
            case "OUT008":
                result["LpnId"] = ctx.Get("lpnId") ?? "78000000-0000-0000-0000-000000000101";
                break;
            case "OUT009":
                result["TripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["LoadedLpnIds"] = new List<string> { ctx.Get("lpnId") ?? "78000000-0000-0000-0000-000000000101" };
                break;
            case "OUT010":
                result["SealCode"] = ctx.Get("sealNumber") ?? "SEAL-2026-001";
                break;
            case "DEL005":
                result["StopId"] = ctx.Get("stopId") ?? "30000000-0000-0000-0000-000000000001";
                result["TripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["CustomerId"] = ctx.Get("customerId") ?? "33333333-3333-3333-3333-333333333333";
                break;
            case "DEL008":
                result["StopId"] = ctx.Get("stopId") ?? "30000000-0000-0000-0000-000000000001";
                result["TripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["CustomerId"] = ctx.Get("customerId") ?? "33333333-3333-3333-3333-333333333333";
                result["RejectedQuantity"] = "1";
                result["RejectionReason"] = "TEMPERATURE_VIOLATION_OSD";
                result["IsReturnToWarehouse"] = "true";
                break;
            case "DEL009":
                result["EpodId"] = ctx.Get("epodId") ?? "86000000-0000-0000-0000-000000000001";
                result["TransactionCode"] = $"TXN-QR-{Guid.NewGuid():N}"[..14].ToUpper();
                result["BankReference"] = "VIETCOMBANK-QR-PAYMENT";
                break;
            case "DEL010":
                result["tripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["totalAmount"] = 1500000m;
                result["notes"] = "Handover full collected COD to warehouse accountant";
                result["receiverStaffId"] = ctx.Get("accountantUserId") ?? "11111111-1111-1111-1111-111111111111";
                break;
            case "DEL011":
                result["StopId"] = ctx.Get("stopId") ?? "30000000-0000-0000-0000-000000000001";
                result["Reason"] = "Customer unreachable by phone and door locked";
                result["WaitingTimeMinutes"] = "45";
                break;

            case "RET001":
                result["StopId"] = ctx.Get("stopId") ?? "30000000-0000-0000-0000-000000000001";
                result["TripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["CustomerId"] = ctx.Get("customerId") ?? "33333333-3333-3333-3333-333333333333";
                result["RejectionReason"] = "TEMPERATURE_VIOLATION_FULL_REJECT";
                result["IsReturnToWarehouse"] = "true";
                break;
            case "RET003":
                result["warehouseId"] = ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001";
                result["lpnCodes"] = new List<string> { ctx.Get("lpnCode") ?? "LPN-RETURN-TEST" };
                break;
            case "RET007":
                result["lpnId"] = ctx.Get("lpnId") ?? "78000000-0000-0000-0000-000000000101";
                result["accept"] = true;
                result["penaltyAmount"] = 0m;
                result["penaltyReason"] = "Accepted discrepancy after warehouse supervisor review";
                break;
            case "CLM001":
                result["OrderId"] = ctx.Get("orderId") ?? "75000000-0000-0000-0000-000000000101";
                result["ClaimType"] = "DAMAGE";
                result["Description"] = "Pharmaceutical products thawed due to reefer failure";
                break;
            case "CLM004":
                result["claimId"] = ctx.Get("claimId") ?? "95000000-0000-0000-0000-000000000001";
                result["approvedAmount"] = 4500000m;
                result["notes"] = "Approved after telemetry and evidence verification";
                break;
            case "CLM005":
                result["claimId"] = ctx.Get("claimId") ?? "95000000-0000-0000-0000-000000000001";
                result["paymentReference"] = $"BNK-PAY-{Guid.NewGuid():N}"[..12].ToUpper();
                result["payoutAmount"] = 4500000m;
                result["notes"] = "Payout processed to customer bank account";
                break;

            case "INC003":
                result["TripId"] = ctx.Get("tripId") ?? "99000000-0000-0000-0000-000000000101";
                result["IncidentType"] = "VEHICLE_BREAKDOWN";
                result["Severity"] = "HIGH";
                result["Description"] = "Cooling unit compressor failed on highway QL1A";
                result["CurrentLatitude"] = "10.8231";
                result["CurrentLongitude"] = "106.6297";
                result["DriverPaidAmount"] = "1200000";
                result["RequiresRescue"] = "true";
                break;
            case "INC004":
                result["IncidentId"] = ctx.Get("incidentId") ?? "98000000-0000-0000-0000-000000000001";
                result["EvidenceType"] = "INCIDENT_PHOTO";
                result["Description"] = "Dashboard error photo showing cooling failure";
                break;
            case "INC005":
                result["incidentId"] = ctx.Get("incidentId") ?? "98000000-0000-0000-0000-000000000001";
                result["approvedAmount"] = 1200000m;
                result["notes"] = "Approved roadside repair expense";
                break;
            case "INC006":
                result["incidentId"] = ctx.Get("incidentId") ?? "98000000-0000-0000-0000-000000000001";
                result["reimbursedAmount"] = 1200000m;
                result["note"] = "Reimbursed emergency roadside repair";
                break;
            case "INC007":
                result["handlingNote"] = "Cooling stabilized after roadside inspection; continuing trip";
                break;
            case "INC008":
                result["incidentId"] = ctx.Get("incidentId") ?? "98000000-0000-0000-0000-000000000001";
                result["resolutionNote"] = "Truck repaired and goods safely transported to destination";
                break;
            case "INC010":
                result["incidentId"] = ctx.Get("incidentId") ?? "98000000-0000-0000-0000-000000000001";
                result["replacementVehicleId"] = ctx.Get("rescueVehicleId") ?? ctx.Get("vehicleId") ?? "77777777-7777-7777-7777-777777777777";
                result["transloadMinutes"] = 45;
                result["note"] = "Dispatch refrigerated rescue truck to incident scene";
                break;
            case "INC011":
                result["incidentId"] = ctx.Get("incidentId") ?? "98000000-0000-0000-0000-000000000001";
                result["confirmationNote"] = "Transload completed with temperature at -18C";
                break;

            case "FLT001":
            case "FLT002":
                result["truckPlate"] = $"{Random.Shared.Next(50, 99)}C-{Random.Shared.Next(10000, 99999)}";
                result["vehicleType"] = "REEFER_5T";
                result["maxWeight"] = 5000m;
                result["maxCbm"] = 18m;
                result["innerLengthCm"] = 430m;
                result["innerWidthCm"] = 200m;
                result["innerHeightCm"] = 220m;
                result["minTemp"] = -25.0m;
                result["maxTemp"] = 10.0m;
                result["status"] = "ACTIVE";
                break;
            case "FLT006":
                result["vehicleId"] = ctx.Get("vehicleId") ?? "77777777-7777-7777-7777-777777777777";
                result["maintenanceType"] = "ROUTINE_SERVICE";
                result["garageName"] = "ColdChainX Service Garage";
                result["description"] = "Scheduled periodic compressor overhaul";
                break;
            case "FLT007":
                result["cost"] = 9200000m;
                result["completionDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
                break;
            case "DRV003":
            case "DRV004":
                var driverSeed = Guid.NewGuid().ToString("N")[..10];
                result["fullName"] = "Test Driver " + driverSeed;
                result["email"] = $"driver.{driverSeed}@coldchain.vn";
                result["identityNumber"] = Random.Shared.Next(100000000, 999999999).ToString();
                result["phoneNumber"] = "09" + Random.Shared.Next(10000000, 99999999);
                result["dateOfBirth"] = "1990-01-15";
                result["joinDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
                result["license"] = new Dictionary<string, object>
                {
                    ["licenseNumber"] = Random.Shared.Next(100000000, 999999999).ToString(),
                    ["licenseClass"] = "FC",
                    ["issueDate"] = "2024-01-01",
                    ["expiryDate"] = "2030-12-31"
                };
                break;

            case "WRK003":
                var workerSeed = Guid.NewGuid().ToString("N")[..10];
                result["username"] = $"whworker.{workerSeed}";
                result["password"] = "Password@123";
                result["fullName"] = "Warehouse Worker " + workerSeed;
                result["email"] = $"whworker.{workerSeed}@coldchain.vn";
                result["phone"] = "09" + Random.Shared.Next(10000000, 99999999);
                result["warehouseId"] = ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001";
                break;
            case "SYS003":
            case "SYS004":
                result["configKey"] = ctx.Get("configKey") ?? "SYSTEM_DEFAULT_TIMEOUT";
                result["configValue"] = "60";
                result["description"] = "System default network and transaction timeout in seconds";
                break;

            default:
                if (ctx.Has("orderId")) result["orderId"] = ctx.Get("orderId")!;
                if (ctx.Has("customerId")) result["customerId"] = ctx.Get("customerId")!;
                if (ctx.Has("routeId")) result["routeId"] = ctx.Get("routeId")!;
                if (ctx.Has("warehouseId")) result["warehouseId"] = ctx.Get("warehouseId")!;
                if (ctx.Has("tripId")) result["tripId"] = ctx.Get("tripId")!;
                break;
        }

        return result;
    }

    private static string ToCamelCase(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLower(s[0]) + s[1..];
}
