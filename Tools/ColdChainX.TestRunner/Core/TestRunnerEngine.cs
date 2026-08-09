using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

public class TestRunner
{
    private readonly HttpClient _http;
    private readonly HttpClient _iotSimHttp;
    private readonly string _baseUrl;
    private readonly EndpointMapper _mapper;
    private TestContext _ctx;
    private readonly bool _verbose;
    private readonly bool _stopOnFail;
    private Dictionary<string, LoginCredential> _credentials = new(StringComparer.OrdinalIgnoreCase);

    public TestRunner(string baseUrl, EndpointMapper mapper, TestContext ctx,
        bool verbose = true, bool stopOnFail = false)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _mapper = mapper;
        _ctx = ctx;
        _verbose = verbose;
        _stopOnFail = stopOnFail;
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _iotSimHttp = new HttpClient { BaseAddress = new Uri("http://localhost:5001"), Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task BootstrapAsync(Dictionary<string, LoginCredential> credentials)
    {
        _credentials = new Dictionary<string, LoginCredential>(credentials, StringComparer.OrdinalIgnoreCase);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n🔐 Bootstrapping authentication across all user roles...");
        Console.ResetColor();

        foreach (var (role, cred) in credentials)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { email = cred.Email, password = cred.Password });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync("/api/auth/login", content);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("accessToken", out var tok))
                        {
                            var token = tok.GetString()!;
                            _ctx.Set($"{role.ToLower()}Token", token);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"   ✓ {role} login OK");
                            Console.ResetColor();

                            if (data.TryGetProperty("userId", out var uid))
                                _ctx.Set($"{role.ToLower()}UserId", uid.ToString());
                            if (data.TryGetProperty("driverId", out var did) &&
                                did.ValueKind != JsonValueKind.Null)
                                _ctx.Set($"{role.ToLower()}DriverId", did.ToString());
                            if (data.TryGetProperty("customerId", out var cid) &&
                                cid.ValueKind != JsonValueKind.Null)
                                _ctx.Set("customerId", cid.ToString());
                        }
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"   ⚠ {role} login failed ({response.StatusCode}): {Trunc(body, 80)}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   ✗ {role} login error: {ex.Message}");
                Console.ResetColor();
            }
        }

        var adminToken = _ctx.GetToken("Admin") ?? _ctx.GetToken("Dispatcher") ?? _ctx.GetToken("WarehouseWorker");
        if (adminToken != null)
        {
            await EnsureSalesTokenAsync(adminToken);
            await FetchRealDbMetadataAsync(adminToken);
        }

        Console.WriteLine();
    }

    public async Task<List<TestResult>> RunAllAsync(List<TestSpec> specs, string? filterModule = null, Action<TestResult>? onTestCompleted = null, CancellationToken cancellationToken = default)
    {
        var results = new List<TestResult>();
        var filteredSpecs = specs.AsEnumerable();

        if (!string.IsNullOrEmpty(filterModule))
        {
            filteredSpecs = specs.Where(s =>
                s.Code.StartsWith(filterModule, StringComparison.OrdinalIgnoreCase));
        }

        var orderedSpecs = filteredSpecs
            .OrderBy(GetWorkflowOrder)
            .ThenBy(s => ParseFunctionNumber(s.Code))
            .ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var baselineContext = _ctx.Clone();

        if (_verbose && orderedSpecs.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Workflow order: " + string.Join(" -> ", orderedSpecs.Select(s => s.Code)));
            Console.ResetColor();
        }

        foreach (var spec in orderedSpecs)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var endpoint = _mapper.Get(spec.Code);
            if (endpoint == null)
            {
                if (_verbose)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[SKIP] {spec.Code} - No endpoint mapping found");
                    Console.ResetColor();
                }
                foreach (var tc in spec.TestCases)
                {
                    var skipRes = TestResult.Skipped(spec.Code, tc.Id, tc.Type, tc.Desc, "No endpoint mapping");
                    results.Add(skipRes);
                    onTestCompleted?.Invoke(skipRes);
                }
                continue;
            }

            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"\n━━━ {spec.Code} - {spec.ClsName}.{spec.FuncName} ━━━");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    {spec.Requirement}");
                Console.WriteLine($"    {endpoint.Method} {endpoint.Url}");
                Console.ResetColor();
            }

            var freshScenarioPerTestCase = RequiresFreshScenarioPerTestCase(spec, endpoint);
            TestContext? functionScenarioContext = null;
            if (!freshScenarioPerTestCase)
            {
                _ctx = baselineContext.Clone();
                await EnsurePrerequisiteStateAsync(spec);
                functionScenarioContext = _ctx.Clone();
                baselineContext.MergeFrom(_ctx);
            }

            foreach (var tc in spec.TestCases)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("\n⚠ Test run cancelled by user.");
                    break;
                }

                _ctx = freshScenarioPerTestCase
                    ? baselineContext.Clone()
                    : functionScenarioContext!.Clone();
                _ctx.Set("testCaseId", tc.Id);
                if (freshScenarioPerTestCase)
                {
                    ResetScenarioContext();
                    await EnsurePrerequisiteStateAsync(spec, tc);
                }

                var result = await RunTestCaseAsync(spec, tc, endpoint);
                if (result.Status == TestStatus.Passed)
                    baselineContext.MergeFrom(_ctx);

                results.Add(result);
                onTestCompleted?.Invoke(result);

                if (result.Status == TestStatus.Failed && _stopOnFail)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n⛔ Stopping on first failure (--stop-on-fail)");
                    Console.ResetColor();
                    return results;
                }
            }
        }

        return results;
    }

    private static bool RequiresFreshScenarioPerTestCase(TestSpec spec, EndpointInfo endpoint)
    {
        if (string.Equals(endpoint.Method, "GET", StringComparison.OrdinalIgnoreCase))
            return false;

        var prefix = Regex.Match(spec.Code, "^[A-Za-z]+").Value.ToUpperInvariant();
        return prefix is "DSP" or "OUT" or "DEL" or "RET" or "CLM" or "INC" or "WRK";
    }

    private static int GetWorkflowOrder(TestSpec spec)
    {
        var prefix = Regex.Match(spec.Code, "^[A-Za-z]+").Value.ToUpperInvariant();
        return prefix switch
        {
            "USR" => 10,
            "AUTH" => 10,
            "ROU" => 20,
            "WTR" => 21,
            "CAT" => 22,
            "WH" => 23,
            "ORD" => 30,
            "QOT" => 40,
            "CTR" => 50,
            "DRV" => 60,
            "FLT" => 61,
            "IOT" => 62,
            "INB" => 70,
            "DSP" => 80,
            "OUT" => 90,
            "MON" => 100,
            "DEL" => 120,
            "RET" => 130,
            "CLM" => 140,
            "INV" => 150,
            "ACC" => 160,
            "INC" => 170,
            "WRK" => 180,
            "NTF" => 190,
            "SYS" => 200,
            _ => 999
        };
    }

    private static int ParseFunctionNumber(string code)
    {
        var match = Regex.Match(code, @"\d+");
        return match.Success && int.TryParse(match.Value, out var number) ? number : int.MaxValue;
    }

    private void ResetScenarioContext()
    {
        foreach (var key in new[]
        {
            "tripId", "orderId", "lpnId", "epodId", "claimId", "incidentId",
            "ticketId", "assignmentId", "outboundOrderId", "outboundId", "receiptId", "asnId"
        })
        {
            _ctx.Remove(key);
        }
    }

    private async Task<TestResult> RunTestCaseAsync(TestSpec spec, TestCaseSpec tc, EndpointInfo endpoint)
    {
        _ctx.Set("testCaseId", tc.Id);
        if (_verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [{tc.Id}] ({tc.Type}) {Trunc(tc.Desc, 60)}... ");
            Console.ResetColor();
        }

        var token = ResolveToken(endpoint, tc, spec);

        try
        {
            var request = RequestBuilder.Build(endpoint, spec, tc, _ctx, token);

            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine();
                Console.WriteLine($"     → {request.Method} {_baseUrl}{request.RequestUri}");
                Console.ResetColor();
            }

            var sw = Stopwatch.StartNew();
            var response = await _http.SendAsync(request);
            sw.Stop();

            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && _credentials.Count > 0)
            {
                if (_verbose)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("     token expired/invalid; refreshing auth and retrying once...");
                    Console.ResetColor();
                }

                await BootstrapAsync(_credentials);
                token = ResolveToken(endpoint, tc, spec);
                request = RequestBuilder.Build(endpoint, spec, tc, _ctx, token);

                sw.Restart();
                response = await _http.SendAsync(request);
                sw.Stop();
                body = await response.Content.ReadAsStringAsync();
            }

            if (response.IsSuccessStatusCode)
            {
                ResponseExtractor.Extract(_ctx, spec.Code, body);
            }

            var result = ResponseValidator.Validate(response, body, spec, tc, sw.ElapsedMilliseconds);

            if (result.Status == TestStatus.Passed)
            {
                if (_verbose)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"     ✓ PASS ({(int)response.StatusCode}) [{sw.ElapsedMilliseconds}ms]");
                    Console.ResetColor();
                }
                return result;
            }
            else
            {
                if (_verbose)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"     ✗ FAIL: {result.Message} (Got: {(int)response.StatusCode}) [{sw.ElapsedMilliseconds}ms]");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"       Response: {Trunc(body, 200)}");
                    Console.ResetColor();
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"     ✗ ERROR: {ex.Message}");
                Console.ResetColor();
            }
            return TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc, ex.Message, 0, null, 0);
        }
    }

    private string? ResolveToken(EndpointInfo endpoint, TestCaseSpec tc, TestSpec spec)
    {
        bool expectsNoAuth = (tc.Type != "Normal" && tc.Type != "N" && (
            tc.Desc.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            tc.Desc.Contains("without token", StringComparison.OrdinalIgnoreCase) ||
            tc.Desc.Contains("no token", StringComparison.OrdinalIgnoreCase) ||
            tc.Desc.Contains("missing token", StringComparison.OrdinalIgnoreCase) ||
            tc.Desc.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ||
            tc.Desc.Contains("unauthenticated", StringComparison.OrdinalIgnoreCase)
        ));

        if (expectsNoAuth) return null;

        if (tc.Desc.Contains("without", StringComparison.OrdinalIgnoreCase) && tc.Desc.Contains("role", StringComparison.OrdinalIgnoreCase))
        {
            return _ctx.GetToken("Driver") ?? _ctx.GetToken("Customer");
        }

        if (spec.Code == "CTR009" && tc.Type is "N")
        {
            return _ctx.GetToken("Customer");
        }
        if ((spec.Code == "CTR015" || spec.Code == "CTR016") && tc.Type is "N")
        {
            return _ctx.GetToken("Customer");
        }
        if (spec.Code == "QOT009" && tc.Type is "N")
        {
            return _ctx.GetToken("Customer");
        }

        if (endpoint.AuthRole == "Anonymous") return null;

        return _ctx.GetToken(endpoint.AuthRole);
    }

    private async Task EnsurePrerequisiteStateAsync(TestSpec spec, TestCaseSpec? tc = null)
    {
        try
        {
            var adminToken = _ctx.GetToken("Admin") ?? _ctx.GetToken("Dispatcher") ?? _ctx.GetToken("WarehouseWorker");
            var custToken = _ctx.GetToken("Customer") ?? adminToken;
            if (adminToken == null) return;

            if (spec.Code == "WH002" || spec.Code == "WH003")
            {
                await EnsureWarehouseContextAsync(adminToken);
            }

            if (spec.Code == "USR008" || spec.Code == "USR010")
            {
                var delReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/99992222-2222-2222-2222-222222229999");
                delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                await _http.SendAsync(delReq);
            }
            else if (spec.Code == "USR009")
            {
                var resReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/99991111-1111-1111-1111-111111119999/restore");
                resReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                await _http.SendAsync(resReq);
            }

            if (spec.Code.StartsWith("WTR") || spec.Code == "ROU006" || spec.Code == "ROU007" || spec.Code == "ROU008" || spec.Code == "ROU009" || spec.Code == "ROU010" || spec.Code == "ROU011" || spec.Code == "ROU012" || spec.Code == "ROU013" || spec.Code == "ROU014" || spec.Code == "ORD001")
            {
                await CreateRouteStopAndScheduleAsync(adminToken);
            }

            if (spec.Code == "ORD002" || spec.Code == "ORD003" || spec.Code == "ORD004")
            {
                var pId = await CreatePendingOrderAsync(custToken!);
                if (!string.IsNullOrEmpty(pId)) _ctx.Set("orderId", pId);
            }
            else if (spec.Code == "ORD008" || spec.Code == "ORD011")
            {
                var aId = await CreateApprovedOrderAsync(custToken!, adminToken);
                if (!string.IsNullOrEmpty(aId)) _ctx.Set("orderId", aId);
            }
            else if (spec.Code == "QOT001" || spec.Code == "QOT002" || spec.Code == "QOT006")
            {
                var newOrderId = await CreateApprovedOrderAsync(custToken!, adminToken);
                if (!string.IsNullOrEmpty(newOrderId)) _ctx.Set("orderId", newOrderId);
            }
            else if (spec.Code == "QOT007" || spec.Code == "QOT008")
            {
                var (ordId, qotId) = await CreateDraftQuotationAsync(custToken!, adminToken);
                if (!string.IsNullOrEmpty(qotId))
                {
                    _ctx.Set("orderId", ordId!);
                    _ctx.Set("quoteId", qotId!);
                    _ctx.Set("quotationId", qotId!);
                }
            }
            else if (spec.Code == "QOT009")
            {
                var (ordId, qotId) = await CreateSentQuotationAsync(custToken!, adminToken);
                if (!string.IsNullOrEmpty(qotId))
                {
                    _ctx.Set("orderId", ordId!);
                    _ctx.Set("quoteId", qotId!);
                    _ctx.Set("quotationId", qotId!);
                }
            }

            if (spec.Code == "CTR001")
            {
                var (ordId, qotId) = await CreateAcceptedQuotationAsync(custToken!, adminToken);
                if (!string.IsNullOrEmpty(qotId))
                {
                    _ctx.Set("orderId", ordId!);
                    _ctx.Set("quoteId", qotId!);
                    _ctx.Set("quotationId", qotId!);
                }
            }
            else if (spec.Code == "CTR010" || spec.Code == "CTR011")
            {
                await CreateInboundQcAsync(custToken!, adminToken, true);
            }
            else if (spec.Code == "CTR012" || spec.Code == "CTR013" || spec.Code == "CTR014")
            {
                await CreateDraftAppendixAsync(custToken!, adminToken);
            }
            else if (spec.Code == "CTR015" || spec.Code == "CTR016")
            {
                await CreateSentAppendixAsync(custToken!, adminToken);
            }
            else if (spec.Code == "CTR017")
            {
                await CreateAcceptedAppendixAsync(custToken!, adminToken);
            }

            if (spec.Code == "INB001")
            {
                var (ordId, _, _) = await CreateSignedContractAsync(custToken!, adminToken);
                if (!string.IsNullOrEmpty(ordId)) _ctx.Set("orderId", ordId);
            }
            else if (spec.Code == "INB003" || spec.Code == "INB004" || spec.Code == "INB005")
            {
                await CreateAsnAsync(custToken!, adminToken);
            }
            else if (spec.Code == "INB006")
            {
                await CreateInboundQcAsync(custToken!, adminToken, true);
            }
            else if (spec.Code == "INB007" || spec.Code == "INB008")
            {
                await CreateInboundQcAsync(custToken!, adminToken, false);
            }

            if (spec.Code.StartsWith("OUT"))
            {
                if (spec.Code == "OUT007")
                    await EnsurePlannedTripAsync(custToken!, adminToken);
                else if (spec.Code == "OUT008")
                    await EnsurePickingTripAsync(custToken!, adminToken);
                else if (spec.Code == "OUT009")
                    await EnsurePickedTripAsync(custToken!, adminToken);
                else if (spec.Code == "OUT010")
                    await EnsureLoadedTripAsync(custToken!, adminToken);
                else
                    await CreateOutboundOrderAsync(custToken!, adminToken);
            }

            if (spec.Code.StartsWith("DSP") || spec.Code.StartsWith("MON") || spec.Code.StartsWith("IOT"))
            {
                await EnsureVehicleWithIotAndDriverAsync(adminToken);
                if (spec.Code == "MON004")
                    await EnsureTripInTransitAsync(adminToken);
                if (spec.Code == "MON007")
                    await EnsureFreshVehicleAsync(adminToken);
                if (spec.Code == "MON007")
                    await EnsureAssignableIotDevicesAsync(adminToken);
                if (spec.Code == "IOT003" && tc?.Id == "UTCID02")
                {
                    var duplicateCode = "IOT-TRK-001";
                    await CreateIotDeviceAsync(adminToken, duplicateCode, "ACTIVE");
                    _ctx.Set("deviceCode", duplicateCode);
                }
                if (spec.Code == "IOT005")
                    await EnsureDeletableIotDeviceAsync(adminToken);

                if (spec.Code == "DSP001")
                {
                    ClearDispatchBuildContext();
                    await CreateRouteStopAndScheduleAsync(adminToken);
                    await CreateInboundQcAsync(custToken!, adminToken, false);
                    await TrySelectReadyDispatchLpnAsync(adminToken);
                }
                else if (spec.Code == "DSP002" || spec.Code == "DSP003" || spec.Code == "DSP005")
                    await EnsurePlannedTripAsync(custToken!, adminToken);
                else if (spec.Code == "DSP004")
                    await EnsureLoadedTripAsync(custToken!, adminToken);

                if (spec.Code == "DSP003" || spec.Code.StartsWith("MON"))
                {
                    await TriggerIotSimulatorActivationAsync();
                }
            }

            if (spec.Code.StartsWith("DEL"))
            {
                await EnsureTripInTransitAsync(adminToken);
                if (spec.Code == "DEL005" || spec.Code == "DEL008" || spec.Code == "DEL011")
                    await EnsureStopCheckInAsync(adminToken);
                if (spec.Code == "DEL001" || spec.Code == "DEL006" || spec.Code == "DEL007" || spec.Code == "DEL009")
                    await EnsureEpodAsync(adminToken);
            }

            if (spec.Code.StartsWith("RET") || spec.Code.StartsWith("CLM"))
            {
                await EnsureReturnAndClaimStateAsync(spec.Code, custToken!, adminToken);
            }

            if (spec.Code.StartsWith("INV") || spec.Code.StartsWith("ACC") || spec.Code.StartsWith("INC"))
            {
                await EnsureTripInTransitAsync(adminToken);
                if (spec.Code.StartsWith("ACC"))
                    await EnsureInvoiceContextAsync(adminToken);
                if (spec.Code.StartsWith("INC"))
                    await EnsureIncidentContextAsync(spec.Code, adminToken);
            }

            if (spec.Code.StartsWith("FLT") || spec.Code.StartsWith("DRV"))
            {
                var testCaseType = tc?.Type?.Trim();
                var isNormalCase = string.Equals(testCaseType, "N", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(testCaseType, "Normal", StringComparison.OrdinalIgnoreCase);
                if (spec.Code == "FLT003")
                {
                    _ctx.Remove("tripId");
                    _ctx.Remove("vehicleId");
                    _ctx.Remove("deviceCode");
                    await EnsureFreshVehicleAsync(adminToken);
                    var deleteVehicleId = _ctx.Get("vehicleId");
                    if (!string.IsNullOrWhiteSpace(deleteVehicleId))
                        _ctx.Set("deleteVehicleId", deleteVehicleId);
                    if (!isNormalCase &&
                        tc?.Desc.Contains("active", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        await EnsureTripInTransitAsync(adminToken);
                        deleteVehicleId = _ctx.Get("vehicleId");
                        if (!string.IsNullOrWhiteSpace(deleteVehicleId))
                            _ctx.Set("deleteVehicleId", deleteVehicleId);
                    }
                }
                else
                {
                    await EnsureVehicleWithIotAndDriverAsync(adminToken);
                    if (spec.Code == "DRV005" && isNormalCase)
                        await EnsureFreshDriverAsync(adminToken);
                    else if (spec.Code == "DRV005" || spec.Code == "FLT003")
                        await EnsureTripInTransitAsync(adminToken);
                }
                if (spec.Code == "FLT007" && isNormalCase)
                    await EnsureFreshVehicleAsync(adminToken);
                if (spec.Code == "FLT007" || spec.Code == "FLT008" || spec.Code == "FLT009")
                    await EnsureMaintenanceTicketAsync(adminToken);
            }

            if (spec.Code.StartsWith("NTF"))
            {
                await EnsureNotificationContextAsync(adminToken);
            }

            if (spec.Code.StartsWith("WRK"))
            {
                await EnsureWorkAssignmentAsync(adminToken);
                if (spec.Code == "WRK006")
                {
                    var curId = _ctx.Get("assignmentId");
                    if (!string.IsNullOrEmpty(curId))
                    {
                        var workerToken = _ctx.GetToken("WarehouseOperator") ?? _ctx.GetToken("WarehouseWorker") ?? adminToken;
                        var startReq = new HttpRequestMessage(HttpMethod.Put, $"/api/work-assignments/{curId}/start");
                        startReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", workerToken);
                        await _http.SendAsync(startReq);
                    }
                }
            }
        }
        catch
        {
        }
    }

    private async Task TriggerIotSimulatorActivationAsync()
    {
        try
        {
            var deviceCode = _ctx.Get("deviceCode") ?? "IOT-TRK-001";
            var actContent = new StringContent("{\"deviceCode\":\"" + deviceCode + "\",\"isOnline\":true}", Encoding.UTF8, "application/json");
            await _iotSimHttp.PostAsync("/api/iot/activate", actContent);

            var streamContent = new StringContent("{\"intervalMs\":1000,\"tempMin\":-20.0,\"tempMax\":-15.0}", Encoding.UTF8, "application/json");
            await _iotSimHttp.PostAsync($"/api/iot/{deviceCode}/stream", streamContent);
        }
        catch
        {
        }
    }

    private async Task EnsureVehicleWithIotAndDriverAsync(string adminToken)
    {
        try
        {
            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var warehouseId = _ctx.Get("warehouseId");

            var usingWarehouseVehicleLookup = !string.IsNullOrWhiteSpace(warehouseId);
            var vehReq = new HttpRequestMessage(HttpMethod.Get,
                usingWarehouseVehicleLookup
                    ? $"/api/Dispatch/lookup/vehicles/by-warehouse/{warehouseId}"
                    : "/api/vehicles?pageSize=10");
            vehReq.Headers.Authorization = authHeader;
            var vehRes = await _http.SendAsync(vehReq);
            if (vehRes.IsSuccessStatusCode)
            {
                var body = await vehRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var items = ExtractItems(doc.RootElement);
                var selectedVehicleMatchesWarehouse = false;
                if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
                {
                    var vehicle = usingWarehouseVehicleLookup
                        ? items[0]
                        : items.EnumerateArray().FirstOrDefault(item => IsActiveVehicleAtWarehouse(item, warehouseId));
                    selectedVehicleMatchesWarehouse = usingWarehouseVehicleLookup && vehicle.ValueKind != JsonValueKind.Undefined;
                    if (vehicle.ValueKind == JsonValueKind.Undefined)
                    {
                        vehicle = items.EnumerateArray()
                        .FirstOrDefault(item => item.TryGetProperty("status", out var status)
                            && status.ValueKind == JsonValueKind.String
                            && string.Equals(status.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase));
                    }
                    if (vehicle.ValueKind == JsonValueKind.Undefined)
                        vehicle = items[0];

                    var vid = GetStringProperty(vehicle, "vehicleId", "VehicleId", "id", "Id");
                    if (!string.IsNullOrEmpty(vid)) _ctx.Set("vehicleId", vid);
                    var truckPlate = GetStringProperty(vehicle, "truckPlate", "TruckPlate");
                    if (!string.IsNullOrWhiteSpace(truckPlate))
                        _ctx.Set("truckPlate", truckPlate);
                }

                if (!selectedVehicleMatchesWarehouse && !string.IsNullOrWhiteSpace(_ctx.Get("warehouseId")))
                    await EnsureFreshVehicleAsync(adminToken);
            }

            await EnsureIotForCurrentVehicleAsync(adminToken);

            var usingWarehouseDriverLookup = !string.IsNullOrWhiteSpace(warehouseId);
            var drvReq = new HttpRequestMessage(HttpMethod.Get,
                usingWarehouseDriverLookup
                    ? $"/api/Dispatch/lookup/drivers/by-warehouse/{warehouseId}"
                    : "/api/drivers?pageSize=10");
            drvReq.Headers.Authorization = authHeader;
            var drvRes = await _http.SendAsync(drvReq);
            if (drvRes.IsSuccessStatusCode)
            {
                var body = await drvRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var items = ExtractItems(doc.RootElement);
                var selectedDriverMatchesWarehouse = false;
                if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
                {
                    var driver = usingWarehouseDriverLookup
                        ? items[0]
                        : items.EnumerateArray().FirstOrDefault(item => IsActiveDriverAtWarehouse(item, warehouseId));
                    selectedDriverMatchesWarehouse = usingWarehouseDriverLookup && driver.ValueKind != JsonValueKind.Undefined;
                    if (driver.ValueKind == JsonValueKind.Undefined)
                    {
                        driver = items.EnumerateArray()
                            .FirstOrDefault(item => item.TryGetProperty("status", out var status)
                                && status.ValueKind == JsonValueKind.String
                                && string.Equals(status.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase));
                    }
                    if (driver.ValueKind == JsonValueKind.Undefined)
                        driver = items[0];

                    var did = GetStringProperty(driver, "driverId", "DriverId", "id", "Id");
                    if (!string.IsNullOrEmpty(did)) _ctx.Set("driverId", did);
                    var identityNumber = GetStringProperty(driver, "identityNumber", "IdentityNumber");
                    if (!string.IsNullOrWhiteSpace(identityNumber))
                        _ctx.Set("driverIdentityNumber", identityNumber);
                }

                if (!selectedDriverMatchesWarehouse && !string.IsNullOrWhiteSpace(_ctx.Get("warehouseId")))
                    await EnsureFreshDriverAsync(adminToken);
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation error: {Trunc(ex.Message, 300)}");
        }
    }

    private static bool IsActiveVehicleAtWarehouse(JsonElement item, string? warehouseId)
    {
        if (string.IsNullOrWhiteSpace(warehouseId))
            return item.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase);

        return item.TryGetProperty("status", out var statusValue)
            && statusValue.ValueKind == JsonValueKind.String
            && string.Equals(statusValue.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase)
            && item.TryGetProperty("currentLocation", out var currentLocation)
            && currentLocation.ValueKind == JsonValueKind.String
            && string.Equals(currentLocation.GetString(), warehouseId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private static bool IsActiveDriverAtWarehouse(JsonElement item, string? warehouseId)
    {
        if (string.IsNullOrWhiteSpace(warehouseId))
            return item.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase);

        return item.TryGetProperty("status", out var statusValue)
            && statusValue.ValueKind == JsonValueKind.String
            && string.Equals(statusValue.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase)
            && item.TryGetProperty("currentLocation", out var currentLocation)
            && currentLocation.ValueKind == JsonValueKind.String
            && string.Equals(currentLocation.GetString(), warehouseId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureIotForCurrentVehicleAsync(string adminToken)
    {
        try
        {
            var vehicleId = _ctx.Get("vehicleId");
            if (string.IsNullOrWhiteSpace(vehicleId))
                return;

            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/iot-devices?pageSize=100");
            listReq.Headers.Authorization = authHeader;
            var listRes = await _http.SendAsync(listReq);
            if (listRes.IsSuccessStatusCode)
            {
                var body = await listRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var items = ExtractArray(doc.RootElement);
                if (items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("vehicleId", out var assignedVehicleId)
                            && assignedVehicleId.ValueKind == JsonValueKind.String
                            && string.Equals(assignedVehicleId.GetString(), vehicleId, StringComparison.OrdinalIgnoreCase))
                        {
                            if (item.TryGetProperty("deviceCode", out var deviceCode) && deviceCode.ValueKind == JsonValueKind.String)
                                _ctx.Set("deviceCode", deviceCode.GetString()!);
                            return;
                        }
                    }
                }
            }

            var code = $"IOT-TRK-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/iot-devices");
            req.Headers.Authorization = authHeader;
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                deviceCode = code,
                deviceType = "GPS_TEMPERATURE",
                vehicleId,
                status = "ACTIVE",
                samplingRateSeconds = 60
            }), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            if (res.IsSuccessStatusCode)
                _ctx.Set("deviceCode", code);
            else if (_verbose)
                Console.WriteLine($"     prereq IoT attach failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 180)}");
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation send error: {Trunc(ex.Message, 300)}");
        }
    }

    private async Task EnsureAssignableIotDevicesAsync(string adminToken)
    {
        try
        {
            var activeCode = $"IOT-ACT-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
            var inactiveCode = $"IOT-INACT-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

            var activeId = await CreateIotDeviceAsync(adminToken, activeCode, "ACTIVE");
            if (!string.IsNullOrWhiteSpace(activeId))
            {
                _ctx.Set("deviceId", activeId);
                _ctx.Set("deviceCode", activeCode);
            }

            var inactiveId = await CreateIotDeviceAsync(adminToken, inactiveCode, "INACTIVE");
            if (!string.IsNullOrWhiteSpace(inactiveId))
            {
                _ctx.Set("inactiveDeviceId", inactiveId);
                _ctx.Set("inactiveDeviceCode", inactiveCode);
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation accept error: {Trunc(ex.Message, 300)}");
        }
    }

    private async Task EnsureDeletableIotDeviceAsync(string adminToken)
    {
        try
        {
            var code = $"IOT-DEL-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
            var deviceId = await CreateIotDeviceAsync(adminToken, code, "ACTIVE");
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                _ctx.Set("deviceId", deviceId);
                _ctx.Set("deviceCode", code);
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq contract error: {Trunc(ex.Message, 300)}");
        }
    }

    private async Task<string?> CreateIotDeviceAsync(string adminToken, string deviceCode, string status)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/iot-devices");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            deviceCode,
            deviceType = "GPS_TEMPERATURE",
            status,
            samplingRateSeconds = 60
        }), Encoding.UTF8, "application/json");

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
        return data.ValueKind == JsonValueKind.String
            ? data.GetString()
            : data.TryGetProperty("deviceId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
    }

    private async Task EnsureTripInTransitAsync(string adminToken)
    {
        try
        {
            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var custToken = _ctx.GetToken("Customer") ?? adminToken;
            await EnsureLoadedTripAsync(custToken, adminToken, useLoggedInDriver: true);

            var tripId = _ctx.Get("tripId");
            if (!string.IsNullOrWhiteSpace(tripId))
            {
                var sealReq = new HttpRequestMessage(HttpMethod.Post, $"/api/Dispatch/seal-and-dispatch/{tripId}");
                sealReq.Headers.Authorization = authHeader;
                var form = new MultipartFormDataContent();
                form.Add(new StringContent($"SEAL-{Guid.NewGuid():N}"[..12].ToUpperInvariant()), "SealCode");
                sealReq.Content = form;
                await _http.SendAsync(sealReq);
            }

            var selectedTripId = _ctx.Get("tripId");
            if (!string.IsNullOrWhiteSpace(selectedTripId))
            {
                await HydrateTripDetailsAsync(selectedTripId, authHeader);
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq contract send error: {Trunc(ex.Message, 300)}");
        }
    }

    private async Task HydrateTripDetailsAsync(string tripId, AuthenticationHeaderValue authHeader)
    {
        var detailReq = new HttpRequestMessage(HttpMethod.Get, $"/api/Dispatch/trips/{tripId}");
        detailReq.Headers.Authorization = authHeader;
        var detailRes = await _http.SendAsync(detailReq);
        if (!detailRes.IsSuccessStatusCode)
            return;

        var detailBody = await detailRes.Content.ReadAsStringAsync();
        using var detailDoc = JsonDocument.Parse(detailBody);
        var data = detailDoc.RootElement.TryGetProperty("data", out var d) ? d : detailDoc.RootElement;
        HydrateTripActors(data);

        if (data.TryGetProperty("stops", out var stops) && stops.ValueKind == JsonValueKind.Array && stops.GetArrayLength() > 0)
        {
            var stop = stops.EnumerateArray()
                .FirstOrDefault(s => s.TryGetProperty("stopType", out var type)
                    && type.GetString()?.Contains("DELIVERY", StringComparison.OrdinalIgnoreCase) == true);

            if (stop.ValueKind == JsonValueKind.Undefined)
                stop = stops[0];

            if (stop.TryGetProperty("stopId", out var stopId) && stopId.ValueKind == JsonValueKind.String)
                _ctx.Set("stopId", stopId.GetString()!);
        }

        if (data.TryGetProperty("orders", out var orders) && orders.ValueKind == JsonValueKind.Array && orders.GetArrayLength() > 0)
        {
            var order = orders[0];
            if (order.TryGetProperty("orderId", out var orderId) && orderId.ValueKind == JsonValueKind.String)
                _ctx.Set("orderId", orderId.GetString()!);

            if (order.TryGetProperty("customer", out var customer)
                && customer.ValueKind == JsonValueKind.Object
                && customer.TryGetProperty("customerId", out var customerId)
                && customerId.ValueKind == JsonValueKind.String)
            {
                _ctx.Set("customerId", customerId.GetString()!);
            }
        }

        if (data.TryGetProperty("lpns", out var lpns) && lpns.ValueKind == JsonValueKind.Array && lpns.GetArrayLength() > 0)
        {
            var lpn = lpns[0];
            if (lpn.TryGetProperty("lpnId", out var lpnId) && lpnId.ValueKind == JsonValueKind.String)
                _ctx.Set("lpnId", lpnId.GetString()!);
        }
    }

    private async Task EnsureEpodAsync(string adminToken)
    {
        try
        {
            var driverToken = _ctx.GetToken("Driver") ?? adminToken;
            var orderId = _ctx.Get("orderId");
            if (!string.IsNullOrWhiteSpace(orderId) && await TryHydrateEpodByOrderAsync(driverToken, orderId))
                return;

            var tripId = _ctx.Get("tripId");
            var stopId = _ctx.Get("stopId");
            var customerId = _ctx.Get("customerId");
            if (string.IsNullOrWhiteSpace(tripId) ||
                string.IsNullOrWhiteSpace(stopId) ||
                string.IsNullOrWhiteSpace(customerId))
                return;

            var imageBytes = ReadSampleFileBytes("*.png", "*.jpg", "*.jpeg")
                ?? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
            await EnsureStopCheckInAsync(adminToken);

            var confirmReq = new HttpRequestMessage(HttpMethod.Post, $"/api/Delivery/stops/{stopId}/confirm-handover");
            confirmReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
            var confirmForm = new MultipartFormDataContent();
            confirmForm.Add(new StringContent(tripId), "TripId");
            confirmForm.Add(new StringContent(customerId), "CustomerId");
            var signatureContent = new ByteArrayContent(imageBytes);
            signatureContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            confirmForm.Add(signatureContent, "SignatureFile", "signature.jpg");
            confirmReq.Content = confirmForm;

            var confirmRes = await _http.SendAsync(confirmReq);
            if (confirmRes.IsSuccessStatusCode)
            {
                var body = await confirmRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
                if (data.TryGetProperty("epodId", out var epodId) && epodId.ValueKind == JsonValueKind.String)
                    _ctx.Set("epodId", epodId.GetString()!);
            }
            else if (_verbose)
            {
                Console.WriteLine($"     prereq handover failed ({(int)confirmRes.StatusCode}): {Trunc(await confirmRes.Content.ReadAsStringAsync(), 220)}");
            }

            orderId = _ctx.Get("orderId");
            if (!string.IsNullOrWhiteSpace(orderId))
                await TryHydrateEpodByOrderAsync(driverToken, orderId);
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation error: {Trunc(ex.Message, 300)}");
        }
    }

    private async Task EnsureStopCheckInAsync(string adminToken)
    {
        var driverToken = _ctx.GetToken("Driver") ?? adminToken;
        var stopId = _ctx.Get("stopId");
        if (string.IsNullOrWhiteSpace(stopId))
            return;

        var checkInReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stops/{stopId}/check-ins");
        checkInReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
        var checkInForm = new MultipartFormDataContent();
        var imageBytes = ReadSampleFileBytes("*.png", "*.jpg", "*.jpeg")
            ?? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        var proofContent = new ByteArrayContent(imageBytes);
        proofContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        checkInForm.Add(proofContent, "ProofImageFile", "checkin.jpg");
        checkInReq.Content = checkInForm;
        var checkInRes = await _http.SendAsync(checkInReq);
        if (!checkInRes.IsSuccessStatusCode && _verbose)
            Console.WriteLine($"     prereq check-in failed ({(int)checkInRes.StatusCode}): {Trunc(await checkInRes.Content.ReadAsStringAsync(), 180)}");
    }

    private async Task<bool> TryHydrateEpodByOrderAsync(string token, string orderId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/Delivery/orders/{orderId}/epod");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return false;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
        if (data.TryGetProperty("epodId", out var epodId) && epodId.ValueKind == JsonValueKind.String)
        {
            _ctx.Set("epodId", epodId.GetString()!);
            return true;
        }

        return false;
    }

    private async Task EnsureInvoiceContextAsync(string adminToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/invoices?pageSize=10");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return;

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var items = ExtractArray(doc.RootElement);
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return;

            var first = items[0];
            if (first.TryGetProperty("invoiceId", out var invoiceId) && invoiceId.ValueKind == JsonValueKind.String)
                _ctx.Set("invoiceId", invoiceId.GetString()!);
        }
        catch { }
    }

    private async Task EnsureIncidentContextAsync(string code, string adminToken)
    {
        try
        {
            var driverToken = _ctx.GetToken("Driver") ?? adminToken;
            var requiresRescue = code is "INC009" or "INC010" or "INC011";
            var driverPaidAmount = code is "INC005" or "INC006" ? "1200000" : "0";

            var incidentId = await CreateIncidentAsync(driverToken, requiresRescue, driverPaidAmount);
            if (string.IsNullOrWhiteSpace(incidentId)) return;
            _ctx.Set("incidentId", incidentId);

            if (code == "INC006")
            {
                await ApproveIncidentExpenseAsync(adminToken, incidentId);
            }

            if (code == "INC008")
            {
                await ContinueIncidentTripAsync(driverToken, incidentId);
            }

            if (code is "INC009" or "INC010" or "INC011")
            {
                var rescueVehicleId = await SelectRescueCandidateAsync(adminToken, incidentId);
                if (string.IsNullOrWhiteSpace(rescueVehicleId) && code is "INC010" or "INC011")
                    rescueVehicleId = await CreateRescueVehicleAsync(adminToken);
                if (!string.IsNullOrWhiteSpace(rescueVehicleId))
                    _ctx.Set("rescueVehicleId", rescueVehicleId);

                if (code == "INC011" && !string.IsNullOrWhiteSpace(rescueVehicleId))
                {
                    await DispatchRescueAsync(adminToken, incidentId, rescueVehicleId);
                }
            }
        }
        catch { }
    }

    private async Task<string?> CreateRescueVehicleAsync(string adminToken)
    {
        var originalVehicleId = _ctx.Get("vehicleId");
        try
        {
            await EnsureFreshVehicleAsync(adminToken, maxTemp: 25m);
            var rescueVehicleId = _ctx.Get("vehicleId");
            if (string.IsNullOrWhiteSpace(rescueVehicleId)
                || string.Equals(rescueVehicleId, originalVehicleId, StringComparison.OrdinalIgnoreCase))
                return null;

            await EnsureIotForCurrentVehicleAsync(adminToken);
            return rescueVehicleId;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(originalVehicleId))
                _ctx.Set("vehicleId", originalVehicleId);
        }
    }

    private async Task ContinueIncidentTripAsync(string driverToken, string incidentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incidentId}/continue-trip");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            handlingNote = "Dynamic prerequisite: continue trip before resolving incident"
        }), Encoding.UTF8, "application/json");
        await _http.SendAsync(req);
    }

    private async Task<string?> CreateIncidentAsync(string driverToken, bool requiresRescue, string driverPaidAmount)
    {
        var tripId = _ctx.Get("tripId");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/incidents");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
        var form = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(tripId))
            form.Add(new StringContent(tripId), "TripId");
        form.Add(new StringContent("VEHICLE_BREAKDOWN"), "IncidentType");
        form.Add(new StringContent(requiresRescue ? "HIGH" : "MEDIUM"), "Severity");
        form.Add(new StringContent("Dynamic test incident for cold-chain workflow"), "Description");
        form.Add(new StringContent("10.8231"), "CurrentLatitude");
        form.Add(new StringContent("106.6297"), "CurrentLongitude");
        form.Add(new StringContent(driverPaidAmount), "DriverPaidAmount");
        form.Add(new StringContent(requiresRescue ? "true" : "false"), "RequiresRescue");
        var imageBytes = ReadSampleFileBytes("*.png", "*.jpg", "*.jpeg")
            ?? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        var img = new ByteArrayContent(imageBytes);
        img.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "EvidenceFiles", "incident.jpg");
        req.Content = form;

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
        return data.TryGetProperty("incidentId", out var incidentId) && incidentId.ValueKind == JsonValueKind.String
            ? incidentId.GetString()
            : null;
    }

    private async Task ApproveIncidentExpenseAsync(string adminToken, string incidentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incidentId}/expenses/approve");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            approvedAmount = 1200000m,
            approvalNote = "Approved dynamic test emergency expense"
        }), Encoding.UTF8, "application/json");
        await _http.SendAsync(req);
    }

    private async Task<string?> SelectRescueCandidateAsync(string adminToken, string incidentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/incidents/{incidentId}/rescue-candidates");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = ExtractArray(doc.RootElement);
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return null;

        var first = items[0];
        return first.TryGetProperty("vehicleId", out var vehicleId) && vehicleId.ValueKind == JsonValueKind.String
            ? vehicleId.GetString()
            : null;
    }

    private async Task DispatchRescueAsync(string adminToken, string incidentId, string rescueVehicleId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incidentId}/dispatch-rescue");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            replacementVehicleId = rescueVehicleId,
            transloadMinutes = 45,
            note = "Dynamic test rescue dispatch"
        }), Encoding.UTF8, "application/json");
        await _http.SendAsync(req);
    }

    private async Task EnsurePlannedTripAsync(string custToken, string adminToken, bool useLoggedInDriver = false)
    {
        try
        {
            ClearDispatchBuildContext();

            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            await CreateRouteStopAndScheduleAsync(adminToken);
            await CreateInboundQcAsync(custToken, adminToken, false);

            var lpnId = _ctx.Get("lpnId");
            var scheduleId = _ctx.Get("scheduleId");
            if (string.IsNullOrWhiteSpace(lpnId))
            {
                await TrySelectReadyDispatchLpnAsync(adminToken);
                lpnId = _ctx.Get("lpnId");
                scheduleId = _ctx.Get("scheduleId");
            }

            if (string.IsNullOrWhiteSpace(lpnId) || string.IsNullOrWhiteSpace(scheduleId))
                return;

            await EnsureVehicleWithIotAndDriverAsync(adminToken);
            var vehicleId = _ctx.Get("vehicleId");
            if (useLoggedInDriver)
                await EnsureFreshDriverAsync(adminToken);
            var driverId = useLoggedInDriver ? _ctx.Get("driverDriverId") : null;
            if (string.IsNullOrWhiteSpace(driverId))
            {
                await EnsureFreshDriverAsync(adminToken);
                driverId = _ctx.Get("driverId") ?? _ctx.Get("driverDriverId");
            }
            if (string.IsNullOrWhiteSpace(vehicleId) || string.IsNullOrWhiteSpace(driverId))
                return;

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/Dispatch/manual-dispatch?lpnIds={lpnId}");
            req.Headers.Authorization = authHeader;
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(scheduleId), "ScheduleId");
            form.Add(new StringContent(vehicleId), "VehicleId");
            form.Add(new StringContent(driverId), "DriverIds");
            form.Add(new StringContent(DateTime.UtcNow.AddHours(2).ToString("o")), "PlannedStartTime");
            form.Add(new StringContent(DateTime.UtcNow.AddHours(6).ToString("o")), "PlannedEndTime");
            form.Add(new StringContent(string.Empty), "ScreenshotBase64");
            req.Content = form;

            var res = await _http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
                if (data.TryGetProperty("tripId", out var tid) && tid.ValueKind == JsonValueKind.String)
                {
                    _ctx.Set("tripId", tid.GetString()!);
                    await HydrateTripDetailsAsync(tid.GetString()!, authHeader);
                }
            }
            else if (_verbose)
            {
                Console.WriteLine($"     prereq dispatch failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 220)}");
            }
        }
        catch { }
    }

    private void ClearDispatchBuildContext()
    {
        foreach (var key in new[]
        {
            "tripId", "orderId", "lpnId", "receiptId", "asnId", "epodId", "incidentId"
        })
        {
            _ctx.Remove(key);
        }
    }

    private async Task EnsurePickingTripAsync(string custToken, string adminToken, bool useLoggedInDriver = false)
    {
        try
        {
            await EnsurePlannedTripAsync(custToken, adminToken, useLoggedInDriver);
            var tripId = _ctx.Get("tripId");
            if (string.IsNullOrWhiteSpace(tripId)) return;

            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var startReq = new HttpRequestMessage(HttpMethod.Post, $"/api/Dispatch/trip/{tripId}/start-picking");
            startReq.Headers.Authorization = authHeader;
            await _http.SendAsync(startReq);

            await TrySelectLpnFromLookupAsync($"/api/Outbound/available-lpns?tripId={tripId}", authHeader);
        }
        catch { }
    }

    private async Task EnsurePickedTripAsync(string custToken, string adminToken, bool useLoggedInDriver = false)
    {
        try
        {
            await EnsurePickingTripAsync(custToken, adminToken, useLoggedInDriver);
            var tripId = _ctx.Get("tripId");
            if (string.IsNullOrWhiteSpace(tripId)) return;

            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var ids = await GetLpnIdsFromLookupAsync($"/api/Outbound/available-lpns?tripId={tripId}", authHeader);
            foreach (var id in ids)
            {
                var pickReq = new HttpRequestMessage(HttpMethod.Post, "/api/Outbound/pick");
                pickReq.Headers.Authorization = authHeader;
                pickReq.Content = new StringContent(JsonSerializer.Serialize(new { LpnId = id }), Encoding.UTF8, "application/json");
                await _http.SendAsync(pickReq);
            }

            if (ids.Count > 0)
                _ctx.Set("lpnId", ids[0]);
        }
        catch { }
    }

    private async Task EnsureLoadedTripAsync(string custToken, string adminToken, bool useLoggedInDriver = false)
    {
        try
        {
            await EnsurePickedTripAsync(custToken, adminToken, useLoggedInDriver);
            var tripId = _ctx.Get("tripId");
            if (string.IsNullOrWhiteSpace(tripId)) return;

            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var loadedLpnIds = await GetLpnIdsFromLookupAsync($"/api/Outbound/available-lpns?tripId={tripId}", authHeader);
            if (loadedLpnIds.Count == 0 && !string.IsNullOrWhiteSpace(_ctx.Get("lpnId")))
                loadedLpnIds.Add(_ctx.Get("lpnId")!);

            var loadReq = new HttpRequestMessage(HttpMethod.Post, "/api/Outbound/load-trip");
            loadReq.Headers.Authorization = authHeader;
            loadReq.Content = new StringContent(JsonSerializer.Serialize(new
            {
                TripId = tripId,
                LoadedLpnIds = loadedLpnIds
            }), Encoding.UTF8, "application/json");
            await _http.SendAsync(loadReq);

            await HydrateTripDetailsAsync(tripId, authHeader);
        }
        catch { }
    }

    private async Task<string?> TrySelectTripFromLookupAsync(string url, AuthenticationHeaderValue authHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = authHeader;
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = ExtractArray(doc.RootElement);
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return null;

        var first = items[0];
        HydrateTripActors(first);
        if (first.TryGetProperty("tripId", out var tid) && tid.ValueKind == JsonValueKind.String)
        {
            var id = tid.GetString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                _ctx.Set("tripId", id);
                return id;
            }
        }

        return null;
    }

    private void HydrateTripActors(JsonElement trip)
    {
        if (trip.ValueKind != JsonValueKind.Object) return;

        if (trip.TryGetProperty("vehicleId", out var vehicleId) && vehicleId.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(vehicleId.GetString()))
            _ctx.Set("vehicleId", vehicleId.GetString()!);

        if (trip.TryGetProperty("truckPlate", out var truckPlate) && truckPlate.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(truckPlate.GetString()))
            _ctx.Set("truckPlate", truckPlate.GetString()!);

        if (trip.TryGetProperty("driverId", out var driverId) && driverId.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(driverId.GetString()))
            _ctx.Set("driverId", driverId.GetString()!);

        if (trip.TryGetProperty("drivers", out var drivers) && drivers.ValueKind == JsonValueKind.Array && drivers.GetArrayLength() > 0)
            HydrateDriverFromElement(drivers[0]);

        if (trip.TryGetProperty("tripDrivers", out var tripDrivers) && tripDrivers.ValueKind == JsonValueKind.Array && tripDrivers.GetArrayLength() > 0)
            HydrateDriverFromElement(tripDrivers[0]);
    }

    private void HydrateDriverFromElement(JsonElement driver)
    {
        if (driver.ValueKind != JsonValueKind.Object) return;

        if (driver.TryGetProperty("driverId", out var driverId) && driverId.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(driverId.GetString()))
            _ctx.Set("driverId", driverId.GetString()!);
        else if (driver.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
            _ctx.Set("driverId", id.GetString()!);
        else if (driver.TryGetProperty("driver", out var nested) && nested.ValueKind == JsonValueKind.Object)
            HydrateDriverFromElement(nested);
    }

    private async Task TrySelectLpnFromLookupAsync(string url, AuthenticationHeaderValue authHeader)
    {
        var ids = await GetLpnIdsFromLookupAsync(url, authHeader);
        if (ids.Count > 0)
            _ctx.Set("lpnId", ids[0]);
    }

    private async Task TrySelectReadyDispatchLpnAsync(string adminToken)
    {
        var warehouseId = _ctx.Get("warehouseId");
        var scheduleId = _ctx.Get("scheduleId");
        if (string.IsNullOrWhiteSpace(warehouseId))
            return;

        var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
        var url = $"/api/Dispatch/lookup/lpns-ready?warehouseId={Uri.EscapeDataString(warehouseId)}&pageSize=50";
        if (!string.IsNullOrWhiteSpace(scheduleId))
            url += $"&scheduleId={Uri.EscapeDataString(scheduleId)}";

        if (await TrySelectReadyDispatchLpnFromUrlAsync(url, authHeader))
            return;

        if (!string.IsNullOrWhiteSpace(scheduleId))
            await TrySelectReadyDispatchLpnFromUrlAsync($"/api/Dispatch/lookup/lpns-ready?warehouseId={Uri.EscapeDataString(warehouseId)}&pageSize=50", authHeader);
    }

    private async Task<bool> TrySelectReadyDispatchLpnFromUrlAsync(string url, AuthenticationHeaderValue authHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = authHeader;
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return false;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = ExtractArray(doc.RootElement);
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return false;

        foreach (var first in items.EnumerateArray())
        {
            var id = GetStringProperty(first, "lpnId", "LpnId", "id", "Id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var scheduleId = GetStringProperty(first, "scheduleId", "ScheduleId");
            if (string.IsNullOrWhiteSpace(scheduleId))
                continue;

            _ctx.Set("lpnId", id);
            _ctx.Set("scheduleId", scheduleId);
            var warehouseId = GetStringProperty(first, "warehouseId", "WarehouseId");
            if (!string.IsNullOrWhiteSpace(warehouseId))
                _ctx.Set("warehouseId", warehouseId);
            return true;
        }

        return false;
    }

    private async Task<List<string>> GetLpnIdsFromLookupAsync(string url, AuthenticationHeaderValue authHeader)
    {
        var result = new List<string>();
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = authHeader;
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return result;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = ExtractArray(doc.RootElement);
        if (items.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("lpnId", out var lpnId) && lpnId.ValueKind == JsonValueKind.String)
            {
                var id = lpnId.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(id);
            }
        }

        return result;
    }

    private static JsonElement ExtractArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array)
                return data;
            if (data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                return items;
            if (data.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Array)
                return nested;
        }
        if (root.TryGetProperty("items", out var rootItems) && rootItems.ValueKind == JsonValueKind.Array)
            return rootItems;
        return default;
    }

    private async Task EnsureWorkAssignmentAsync(string adminToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/work-assignments");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var assigneeUserId = _ctx.Get("warehouseoperatorUserId")
                ?? _ctx.Get("warehouseworkerUserId")
                ?? _ctx.Get("workerId")
                ?? "22222222-2222-2222-2222-222222222222";
            var payload = new Dictionary<string, object>
            {
                ["taskType"] = "PUTAWAY",
                ["referenceType"] = "RECEIPT",
                ["referenceId"] = $"REC-{Guid.NewGuid():N}"[..12],
                ["requiredPermissionCode"] = "WORK_ASSIGNMENT.EXECUTE",
                ["assignedToUserId"] = assigneeUserId,
                ["warehouseId"] = _ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001",
                ["priority"] = "NORMAL",
                ["note"] = "Dynamic test work assignment"
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && (data.TryGetProperty("assignmentId", out var aid) || data.TryGetProperty("id", out aid)))
                {
                    var id = aid.GetString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        _ctx.Set("assignmentId", id);
                    }
                }
            }
        }
        catch { }
    }

    private async Task EnsureMaintenanceTicketAsync(string adminToken)
    {
        try
        {
            var vehicleId = _ctx.Get("vehicleId");
            if (string.IsNullOrWhiteSpace(vehicleId))
                return;

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/vehicles/{vehicleId}/maintenance-tickets");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                maintenanceType = "ROUTINE_SERVICE",
                garageName = "ColdChainX Service Garage",
                description = "Dynamic maintenance ticket for test runner"
            }), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                if (_verbose)
                    Console.WriteLine($"     prereq vehicle failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 180)}");
                return;
            }

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            if (data.TryGetProperty("ticketId", out var ticketId) && ticketId.ValueKind == JsonValueKind.String)
            {
                var id = ticketId.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    _ctx.Set("ticketId", id);
            }
        }
        catch { }
    }

    private async Task EnsureFreshVehicleAsync(string adminToken, decimal maxTemp = 10m)
    {
        try
        {
            var seed = Guid.NewGuid().ToString("N")[..8];
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/vehicles");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                truckPlate = $"{Random.Shared.Next(50, 99)}C-{Random.Shared.Next(10000, 99999)}",
                vehicleType = "REEFER_5T",
                maxWeight = 5000m,
                maxCbm = 18m,
                innerLengthCm = 430m,
                innerWidthCm = 200m,
                innerHeightCm = 220m,
                minTemp = -25m,
                maxTemp,
                currentLocation = _ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001",
                registration = new
                {
                    documentNumber = $"REG-{seed}",
                    issuer = "Vietnam Register",
                    issueDate = "2026-01-01",
                    expireDate = "2030-01-01"
                },
                insurance = new
                {
                    documentNumber = $"INS-{seed}",
                    issuer = "ColdChainX Insurance",
                    issueDate = "2026-01-01",
                    expireDate = "2030-01-01"
                },
                cityPermit = new
                {
                    documentNumber = $"PER-{seed}",
                    issuer = "Transport Authority",
                    issueDate = "2026-01-01",
                    expireDate = "2030-01-01"
                },
                status = "ACTIVE"
            }), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                if (_verbose)
                    Console.WriteLine($"     prereq fresh vehicle failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 200)}");
                return;
            }

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            if (data.TryGetProperty("vehicleId", out var vehicleId) && vehicleId.ValueKind == JsonValueKind.String)
            {
                var id = vehicleId.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    _ctx.Set("vehicleId", id);
            }
            if (data.TryGetProperty("truckPlate", out var truckPlate) && truckPlate.ValueKind == JsonValueKind.String)
                _ctx.Set("truckPlate", truckPlate.GetString() ?? seed);
        }
        catch { }
    }

    private async Task EnsureFreshDriverAsync(string adminToken)
    {
        try
        {
            var seed = Guid.NewGuid().ToString("N")[..10];
            var email = $"delete.driver.{seed}@coldchain.vn";
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/drivers");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                fullName = "Delete Test Driver " + seed,
                email,
                identityNumber = Random.Shared.Next(100000000, 999999999).ToString(),
                phoneNumber = "09" + Random.Shared.Next(10000000, 99999999),
                dateOfBirth = "1990-01-15",
                joinDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                currentLocation = _ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001",
                license = new
                {
                    licenseNumber = Random.Shared.Next(100000000, 999999999).ToString(),
                    licenseClass = "FC",
                    issueDate = "2024-01-01",
                    expiryDate = "2030-12-31"
                }
            }), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return;

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            if (data.TryGetProperty("driverId", out var driverId) && driverId.ValueKind == JsonValueKind.String)
            {
                var id = driverId.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _ctx.Set("driverId", id);
                    _ctx.Set("driverDriverId", id);
                }
            }
            if (data.TryGetProperty("identityNumber", out var identity) && identity.ValueKind == JsonValueKind.String)
                _ctx.Set("driverIdentityNumber", identity.GetString() ?? seed);

            await LoginDynamicDriverAsync(email);
        }
        catch { }
    }

    private async Task LoginDynamicDriverAsync(string email)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                email,
                password = "@123@"
            }), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                if (_verbose)
                    Console.WriteLine($"     prereq driver login failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
                return;
            }

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            var token = GetStringProperty(data, "accessToken", "token");
            if (!string.IsNullOrWhiteSpace(token))
                _ctx.Set("driverToken", token);

            var driverId = GetStringProperty(data, "driverId", "DriverId");
            if (!string.IsNullOrWhiteSpace(driverId))
            {
                _ctx.Set("driverId", driverId);
                _ctx.Set("driverDriverId", driverId);
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq driver login error: {Trunc(ex.Message, 160)}");
        }
    }

    private async Task EnsureWarehouseContextAsync(string adminToken)
    {
        try
        {
            var code = $"WH-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/warehouses");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                warehouseCode = code,
                warehouseName = "Dynamic Test Warehouse",
                warehouseType = "COLD",
                address = "123 Test Runner Street",
                maxPallets = 100,
                defaultMinTemp = -25m,
                defaultMaxTemp = 8m,
                status = "ACTIVE"
            }), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return;

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            if (data.TryGetProperty("warehouseId", out var warehouseId) && warehouseId.ValueKind == JsonValueKind.String)
            {
                var id = warehouseId.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    _ctx.Set("warehouseId", id);
            }
            _ctx.Set("warehouseCode", code);
        }
        catch { }
    }

    private async Task EnsureNotificationContextAsync(string adminToken)
    {
        try
        {
            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var userId = _ctx.Get("adminUserId") ?? _ctx.Get("userId") ?? "11111111-1111-1111-1111-111111111111";

            var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/notifications/test");
            createReq.Headers.Authorization = authHeader;
            createReq.Content = new StringContent(JsonSerializer.Serialize(new
            {
                userId,
                title = "Test runner notification",
                body = "Notification generated for mark-as-read test",
                type = "SYSTEM",
                referenceId = $"TR-{Guid.NewGuid():N}"[..12]
            }), Encoding.UTF8, "application/json");
            await _http.SendAsync(createReq);

            var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/notifications?pageSize=10");
            listReq.Headers.Authorization = authHeader;
            var listRes = await _http.SendAsync(listReq);
            if (!listRes.IsSuccessStatusCode) return;

            var body = await listRes.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var items = ExtractItems(doc.RootElement);
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return;

            var id = items[0].TryGetProperty("notiId", out var n) ? n.GetString()
                : items[0].TryGetProperty("notificationId", out n) ? n.GetString()
                : items[0].TryGetProperty("id", out n) ? n.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(id))
                _ctx.Set("notificationId", id);
        }
        catch { }
    }

    private static JsonElement ExtractItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array)
                return data;
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("items", out var dataItems) && dataItems.ValueKind == JsonValueKind.Array)
                    return dataItems;
                if (data.TryGetProperty("data", out var nestedData) && nestedData.ValueKind == JsonValueKind.Array)
                    return nestedData;
            }
        }

        if (root.TryGetProperty("items", out var rootItems) && rootItems.ValueKind == JsonValueKind.Array)
            return rootItems;

        return default;
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

    private async Task EnsureReturnAndClaimStateAsync(string code, string custToken, string adminToken)
    {
        try
        {
            if (code.StartsWith("RET"))
            {
                if (code == "RET007")
                {
                    ClearDispatchBuildContext();
                    await CreateRouteStopAndScheduleAsync(adminToken);
                    await CreateInboundQcAsync(custToken, adminToken, true);
                    return;
                }

                await EnsureTripInTransitAsync(adminToken);
                if (code == "RET001" || code == "RET003" || code == "RET004")
                    await EnsureStopCheckInAsync(adminToken);
                if (code == "RET003" || code == "RET004")
                    await EnsureReturnSlipAsync(adminToken);
                return;
            }

            if (code == "CLM004" || code == "CLM005")
            {
                var claimId = await CreateClaimForTestAsync(custToken, adminToken);
                if (!string.IsNullOrWhiteSpace(claimId))
                {
                    _ctx.Set("claimId", claimId);
                    if (code == "CLM005")
                        await ApproveClaimForPayoutAsync(adminToken, claimId);
                    return;
                }
            }

            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var clmReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/claims?pageSize=10");
            clmReq.Headers.Authorization = authHeader;
            var clmRes = await _http.SendAsync(clmReq);
            if (clmRes.IsSuccessStatusCode)
            {
                var body = await clmRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var items = doc.RootElement.TryGetProperty("data", out var d) ? (d.TryGetProperty("items", out var di) ? di : d) : default;
                if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
                {
                    var cid = items[0].TryGetProperty("claimId", out var c) ? c.GetString() : (items[0].TryGetProperty("id", out c) ? c.GetString() : null);
                    if (!string.IsNullOrEmpty(cid)) _ctx.Set("claimId", cid);
                }
            }
        }
        catch { }
    }

    private async Task EnsureReturnSlipAsync(string adminToken)
    {
        try
        {
            var driverToken = _ctx.GetToken("Driver") ?? adminToken;
            var stopId = _ctx.Get("stopId");
            var tripId = _ctx.Get("tripId");
            var customerId = _ctx.Get("customerId");
            if (string.IsNullOrWhiteSpace(stopId) ||
                string.IsNullOrWhiteSpace(tripId) ||
                string.IsNullOrWhiteSpace(customerId))
                return;

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/Delivery/stops/{stopId}/reject-entire-lpn");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverToken);
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(stopId), "StopId");
            form.Add(new StringContent(tripId), "TripId");
            form.Add(new StringContent(customerId), "CustomerId");
            form.Add(new StringContent("TEMPERATURE_VIOLATION_FULL_REJECT"), "RejectionReason");
            form.Add(new StringContent("true"), "IsReturnToWarehouse");
            var imageBytes = ReadSampleFileBytes("*.png", "*.jpg", "*.jpeg")
                ?? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
            var photo = new ByteArrayContent(imageBytes);
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(photo, "EvidenceImageFile", "return-evidence.jpg");
            req.Content = form;

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode && _verbose)
                Console.WriteLine($"     prereq return slip failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 200)}");
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq return slip error: {Trunc(ex.Message, 200)}");
        }
    }

    private async Task<string?> CreateClaimForTestAsync(string custToken, string adminToken)
    {
        try
        {
            var orderId = await CreateApprovedOrderAsync(custToken, adminToken);
            if (string.IsNullOrWhiteSpace(orderId))
                return null;

            _ctx.Set("orderId", orderId);

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(orderId), "OrderId");
            form.Add(new StringContent("DAMAGE"), "ClaimType");
            form.Add(new StringContent("Dynamic claim for dispatcher/accountant test"), "Description");
            var evidenceBytes = ReadSampleFileBytes("*.png", "*.jpg", "*.jpeg", "*.pdf") ?? Encoding.UTF8.GetBytes("%PDF-1.4 Dynamic claim evidence");
            var evidence = new ByteArrayContent(evidenceBytes);
            evidence.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(evidence, "EvidenceImages", "claim_evidence.pdf");
            req.Content = form;

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return null;

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            return data.TryGetProperty("claimId", out var claimId) && claimId.ValueKind == JsonValueKind.String
                ? claimId.GetString()
                : null;
        }
        catch { return null; }
    }

    private async Task ApproveClaimForPayoutAsync(string adminToken, string claimId)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/claims/{claimId}/dispatcher-approve");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                approvedAmount = 4500000m,
                note = "Approved dynamic test claim for payout"
            }), Encoding.UTF8, "application/json");
            await _http.SendAsync(req);
        }
        catch { }
    }

    private async Task EnsureSalesTokenAsync(string adminToken)
    {
        try
        {
            var email = $"sales.runner.{Guid.NewGuid():N}"[..25] + "@coldchainx.test";
            const string password = "Password@123";
            var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            createReq.Content = new StringContent(JsonSerializer.Serialize(new
            {
                fullName = "Sales Test Runner",
                email,
                password,
                phoneNumber = "0901234567",
                role = "Sales",
                status = "Active"
            }), Encoding.UTF8, "application/json");

            var createRes = await _http.SendAsync(createReq);
            if (!createRes.IsSuccessStatusCode) return;

            var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
            loginReq.Content = new StringContent(JsonSerializer.Serialize(new
            {
                email,
                password
            }), Encoding.UTF8, "application/json");

            var loginRes = await _http.SendAsync(loginReq);
            if (!loginRes.IsSuccessStatusCode) return;

            var body = await loginRes.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;

            if (data.TryGetProperty("accessToken", out var token) && token.ValueKind == JsonValueKind.String)
                _ctx.Set("salesToken", token.GetString()!);
            if (data.TryGetProperty("userId", out var userId) && userId.ValueKind == JsonValueKind.String)
                _ctx.Set("salesUserId", userId.GetString()!);
        }
        catch { }
    }

    private async Task FetchRealDbMetadataAsync(string adminToken)
    {
        try
        {
            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);

            var routeReq = new HttpRequestMessage(HttpMethod.Get, "/api/routes/options");
            routeReq.Headers.Authorization = authHeader;
            var routeRes = await _http.SendAsync(routeReq);
            if (routeRes.IsSuccessStatusCode)
            {
                var body = await routeRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var items = doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array ? d : (doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : default);
                if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
                {
                    bool foundBoth = false;
                    foreach (var item in items.EnumerateArray())
                    {
                        var rid = item.TryGetProperty("routeId", out var r) ? r.GetString() : (item.TryGetProperty("id", out r) ? r.GetString() : null);
                        if (string.IsNullOrEmpty(rid)) continue;
                        if (!rid.StartsWith("10000000-", StringComparison.OrdinalIgnoreCase)) continue;

                        string? sid = null;
                        var schedReq = new HttpRequestMessage(HttpMethod.Get, $"/api/routes/{rid}/schedules");
                        schedReq.Headers.Authorization = authHeader;
                        var schedRes = await _http.SendAsync(schedReq);
                        if (schedRes.IsSuccessStatusCode)
                        {
                            var schedBody = await schedRes.Content.ReadAsStringAsync();
                            using var schedDoc = JsonDocument.Parse(schedBody);
                            var sItems = schedDoc.RootElement.TryGetProperty("data", out var sd)
                                ? (sd.TryGetProperty("items", out var sdi) ? sdi : (sd.TryGetProperty("data", out sdi) ? sdi : sd))
                                : (schedDoc.RootElement.ValueKind == JsonValueKind.Array ? schedDoc.RootElement : default);
                            if (sItems.ValueKind == JsonValueKind.Array && sItems.GetArrayLength() > 0)
                            {
                                var selectedSchedule = sItems.EnumerateArray()
                                    .FirstOrDefault(x =>
                                        x.TryGetProperty("status", out var status) &&
                                        string.Equals(status.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase));
                                if (selectedSchedule.ValueKind != JsonValueKind.Object)
                                    selectedSchedule = sItems[0];

                                sid = selectedSchedule.TryGetProperty("scheduleId", out var s) ? s.GetString() : (selectedSchedule.TryGetProperty("id", out s) ? s.GetString() : null);
                            }
                        }

                        string? stid = null;
                        var stopReq = new HttpRequestMessage(HttpMethod.Get, $"/api/routes/{rid}/stops");
                        stopReq.Headers.Authorization = authHeader;
                        var stopRes = await _http.SendAsync(stopReq);
                        if (stopRes.IsSuccessStatusCode)
                        {
                            var stopBody = await stopRes.Content.ReadAsStringAsync();
                            using var stopDoc = JsonDocument.Parse(stopBody);
                            var stItems = stopDoc.RootElement.TryGetProperty("data", out var std)
                                ? (std.TryGetProperty("items", out var stdi) ? stdi : (std.TryGetProperty("data", out stdi) ? stdi : std))
                                : (stopDoc.RootElement.ValueKind == JsonValueKind.Array ? stopDoc.RootElement : default);
                            if (stItems.ValueKind == JsonValueKind.Array && stItems.GetArrayLength() > 0)
                            {
                                stid = stItems[0].TryGetProperty("stopId", out var st) ? st.GetString() : (stItems[0].TryGetProperty("id", out st) ? st.GetString() : null);
                            }
                        }

                        if (!string.IsNullOrEmpty(sid) && !string.IsNullOrEmpty(stid))
                        {
                            _ctx.Set("routeId", rid);
                            _ctx.Set("scheduleId", sid);
                            _ctx.Set("stopId", stid);
                            _ctx.Set("dropoffStopId", stid);
                            foundBoth = true;
                            break;
                        }
                    }

                    if (!foundBoth)
                        await CreateRouteStopAndScheduleAsync(adminToken);
                }
            }

            var whReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/warehouses");
            whReq.Headers.Authorization = authHeader;
            var whRes = await _http.SendAsync(whReq);
            if (whRes.IsSuccessStatusCode)
            {
                var whBody = await whRes.Content.ReadAsStringAsync();
                using var whDoc = JsonDocument.Parse(whBody);
                var whItems = whDoc.RootElement.TryGetProperty("data", out var wd)
                    ? (wd.TryGetProperty("items", out var wdi) ? wdi : (wd.TryGetProperty("data", out wdi) ? wdi : wd))
                    : (whDoc.RootElement.ValueKind == JsonValueKind.Array ? whDoc.RootElement : default);
                if (whItems.ValueKind == JsonValueKind.Array && whItems.GetArrayLength() > 0)
                {
                    var selectedWarehouse = whItems.EnumerateArray()
                        .FirstOrDefault(x =>
                            x.TryGetProperty("warehouseId", out var id) &&
                            string.Equals(id.GetString(), "87b07384-d113-46c6-950c-619f7e5b32cd", StringComparison.OrdinalIgnoreCase));
                    if (selectedWarehouse.ValueKind != JsonValueKind.Object)
                    {
                        selectedWarehouse = whItems.EnumerateArray()
                            .FirstOrDefault(x =>
                                x.TryGetProperty("status", out var status) &&
                                string.Equals(status.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
                                x.TryGetProperty("createdAt", out var createdAt) &&
                                DateTime.TryParse(createdAt.GetString(), out var created) &&
                                created < new DateTime(2026, 8, 8));
                    }
                    if (selectedWarehouse.ValueKind != JsonValueKind.Object)
                        selectedWarehouse = whItems[0];

                    var wid = selectedWarehouse.TryGetProperty("warehouseId", out var w) ? w.GetString() : (selectedWarehouse.TryGetProperty("id", out w) ? w.GetString() : null);
                    if (!string.IsNullOrEmpty(wid)) _ctx.Set("warehouseId", wid);
                    if (selectedWarehouse.TryGetProperty("warehouseCode", out var code) && !string.IsNullOrWhiteSpace(code.GetString()))
                        _ctx.Set("warehouseCode", code.GetString()!);
                }
            }
        }
        catch { }
    }

    private async Task<string?> CreatePendingOrderAsync(string custToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            var form = new MultipartFormDataContent();
            form.Add(new StringContent("Seeded Dynamic Order"), "Item_Name");
            form.Add(new StringContent("PHARMACEUTICALS"), "Category");
            form.Add(new StringContent("-15"), "Temp_Condition");
            form.Add(new StringContent("1"), "Quantity");
            form.Add(new StringContent("Carton Box"), "Packaging_Type");
            form.Add(new StringContent("10"), "Expected_Weight_KG");
            form.Add(new StringContent("20"), "Length_CM");
            form.Add(new StringContent("20"), "Width_CM");
            form.Add(new StringContent("20"), "Height_CM");
            form.Add(new StringContent("123 Le Loi, District 1, HCMC"), "Dest_Address_Text");
            form.Add(new StringContent("true"), "Is_Stackable");
            form.Add(new StringContent("false"), "Has_Strong_Odor");
            var scheduleId = _ctx.Get("scheduleId") ?? "20000000-0000-0000-0000-000000000001";
            var stopId = _ctx.Get("stopId") ?? "30000000-0000-0000-0000-000000000001";
            form.Add(new StringContent(scheduleId), "Schedule_ID");
            form.Add(new StringContent(stopId), "Dropoff_Stop_ID");
            req.Content = form;
            var res = await _http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.TryGetProperty("data", out var payload) ? payload : doc.RootElement;
                var orderId = GetStringProperty(data, "orderId", "OrderId", "id", "Id");
                if (!string.IsNullOrWhiteSpace(orderId))
                    return orderId;
                if (_verbose)
                    Console.WriteLine($"     prereq order success without orderId: {Trunc(body, 300)}");
            }
            else if (_verbose)
            {
                Console.WriteLine($"     prereq order failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq order error: {Trunc(ex.Message, 160)}");
        }
        return null;
    }

    private async Task<string?> CreateApprovedOrderAsync(string custToken, string adminToken)
    {
        try
        {
            var id = await CreatePendingOrderAsync(custToken);
            if (id == null)
            {
                ClearDispatchBuildContext();
                await CreateRouteStopAndScheduleAsync(adminToken);
                id = await CreatePendingOrderAsync(custToken);
            }
            if (id != null)
            {
                var salesToken = _ctx.GetToken("Sales") ?? adminToken;
                var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{id}/review");
                revReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
                revReq.Content = new StringContent("{\"Action\":\"APPROVE\",\"CustomerNote\":\"Approved for quotation\"}", Encoding.UTF8, "application/json");
                await _http.SendAsync(revReq);
                return id;
            }
        }
        catch { }
        return null;
    }

    private async Task<(string? orderId, string? quoteId)> CreateDraftQuotationAsync(string custToken, string adminToken)
    {
        try
        {
            var orderId = await CreatePendingOrderAsync(custToken);
            if (orderId == null)
            {
                if (_verbose) Console.WriteLine("     prereq quotation skipped: orderId was not created");
                return (null, null);
            }
            var salesToken = _ctx.GetToken("Sales") ?? adminToken;
            var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/review");
            revReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
            revReq.Content = new StringContent("{\"Action\":\"APPROVE\",\"CustomerNote\":\"Approved for quotation\"}", Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(revReq);
            if (res.IsSuccessStatusCode)
            {
                var revBody = await res.Content.ReadAsStringAsync();
                using var revDoc = JsonDocument.Parse(revBody);
                var revData = revDoc.RootElement.TryGetProperty("data", out var rd) ? rd : revDoc.RootElement;
                var reviewedQuoteId = GetStringProperty(revData, "quoteId", "QuoteId", "id", "Id");
                if (!string.IsNullOrWhiteSpace(reviewedQuoteId))
                    return (orderId, reviewedQuoteId);
            }
            else if (_verbose)
            {
                Console.WriteLine($"     prereq order review failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
            }
            var qotReq = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}/quotations");
            qotReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
            var qotRes = await _http.SendAsync(qotReq);
            if (qotRes.IsSuccessStatusCode)
            {
                var qBody = await qotRes.Content.ReadAsStringAsync();
                using var qDoc = JsonDocument.Parse(qBody);
                var qItems = qDoc.RootElement.TryGetProperty("data", out var qd) ? qd : (qDoc.RootElement.ValueKind == JsonValueKind.Array ? qDoc.RootElement : default);
                if (qItems.ValueKind == JsonValueKind.Array && qItems.GetArrayLength() > 0)
                {
                    var qid = GetStringProperty(qItems[0], "quoteId", "QuoteId", "id", "Id");
                    if (!string.IsNullOrEmpty(qid)) return (orderId, qid);
                }
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation accept error: {Trunc(ex.Message, 300)}");
        }
        return (null, null);
    }

    private async Task<(string? orderId, string? quoteId)> CreateSentQuotationAsync(string custToken, string adminToken)
    {
        try
        {
            var (orderId, quoteId) = await CreateDraftQuotationAsync(custToken, adminToken);
            if (quoteId == null)
            {
                if (_verbose) Console.WriteLine("     prereq quotation send skipped: quoteId was not created");
                return (orderId, null);
            }

            var salesToken = _ctx.GetToken("Sales") ?? adminToken;
            var sendReq = new HttpRequestMessage(HttpMethod.Post, $"/api/quotations/{quoteId}/send");
            sendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
            var res = await _http.SendAsync(sendReq);
            if (res.IsSuccessStatusCode) return (orderId, quoteId);
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation send error: {Trunc(ex.Message, 300)}");
        }
        return (null, null);
    }

    private async Task<(string? orderId, string? quoteId)> CreateAcceptedQuotationAsync(string custToken, string adminToken)
    {
        try
        {
            var (orderId, quoteId) = await CreateSentQuotationAsync(custToken, adminToken);
            if (quoteId == null)
            {
                if (_verbose) Console.WriteLine("     prereq quotation accept skipped: quoteId was not created");
                return (orderId, null);
            }

            var accReq = new HttpRequestMessage(HttpMethod.Post, $"/api/quotations/{quoteId}/accept");
            accReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            accReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(accReq);
            if (res.IsSuccessStatusCode) return (orderId, quoteId);
            if (_verbose)
                Console.WriteLine($"     prereq quotation accept failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation accept error: {Trunc(ex.Message, 300)}");
        }
        return (null, null);
    }

    private async Task<(string? orderId, string? quoteId, string? contractId)> CreateContractAsync(string custToken, string adminToken)
    {
        try
        {
            var (orderId, quoteId) = await CreateAcceptedQuotationAsync(custToken, adminToken);
            if (orderId == null)
            {
                if (_verbose) Console.WriteLine("     prereq contract skipped: orderId was not created");
                return (null, null, null);
            }

            var salesToken = _ctx.GetToken("Sales") ?? adminToken;
            var genReq = new HttpRequestMessage(HttpMethod.Post, "/api/contracts/generate");
            genReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
            genReq.Content = new StringContent("{\"orderId\":\"" + orderId + "\"}", Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(genReq);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.TryGetProperty("data", out var payload) ? payload : doc.RootElement;
                var contractId = GetStringProperty(data, "contractId", "ContractId", "id", "Id");
                if (!string.IsNullOrWhiteSpace(contractId))
                    return (orderId, quoteId, contractId);
            }
            else if (_verbose)
            {
                Console.WriteLine($"     prereq contract generate failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation send error: {Trunc(ex.Message, 300)}");
        }
        return (null, null, null);
    }

    private async Task<(string? orderId, string? quoteId, string? contractId)> CreateSentContractAsync(string custToken, string adminToken)
    {
        try
        {
            var (orderId, quoteId, contractId) = await CreateContractAsync(custToken, adminToken);
            if (contractId == null)
            {
                if (_verbose) Console.WriteLine("     prereq contract send skipped: contractId was not created");
                return (orderId, quoteId, null);
            }

            var salesToken = _ctx.GetToken("Sales") ?? adminToken;
            var sendReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contracts/{contractId}/send");
            sendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
            var res = await _http.SendAsync(sendReq);
            if (res.IsSuccessStatusCode) return (orderId, quoteId, contractId);
            if (_verbose)
                Console.WriteLine($"     prereq contract send failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
            return (orderId, quoteId, contractId);
        }
        catch { }
        return (null, null, null);
    }

    private async Task<(string? orderId, string? quoteId, string? contractId)> CreateUploadedContractAsync(string custToken, string adminToken)
    {
        try
        {
            var (orderId, quoteId, contractId) = await CreateSentContractAsync(custToken, adminToken);
            if (contractId == null)
            {
                if (_verbose) Console.WriteLine("     prereq contract upload skipped: contractId was not created");
                return (orderId, quoteId, null);
            }

            var upReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contracts/{contractId}/upload-signed");
            upReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            var form = new MultipartFormDataContent();
            var imagePath = FindLocalUploadImage();
            if (imagePath != null)
            {
                var imageContent = new ByteArrayContent(await File.ReadAllBytesAsync(imagePath));
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetImageContentType(imagePath));
                form.Add(imageContent, "SignedFile", Path.GetFileName(imagePath));
            }
            else
            {
                var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
                var pngContent = new ByteArrayContent(pngBytes);
                pngContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(pngContent, "SignedFile", "signed_contract.png");
            }
            upReq.Content = form;
            var res = await _http.SendAsync(upReq);
            if (res.IsSuccessStatusCode) return (orderId, quoteId, contractId);
            if (_verbose)
                Console.WriteLine($"     prereq contract upload failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
        }
        catch (Exception ex)
        {
            if (_verbose)
                Console.WriteLine($"     prereq quotation accept error: {Trunc(ex.Message, 300)}");
        }
        return (null, null, null);
    }

    private static string? FindLocalUploadImage()
    {
        var roots = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "ColdChainX.API", "wwwroot", "uploads"),
            Path.Combine(Environment.CurrentDirectory, "ColdChainX.API", "wwwroot", "contracts", "signed")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            var file = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path =>
                    path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
            if (file != null) return file;
        }

        return null;
    }

    private static string GetImageContentType(string path)
    {
        if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";
        return "image/png";
    }

    private async Task<(string? orderId, string? quoteId, string? contractId)> CreateSignedContractAsync(string custToken, string adminToken)
    {
        try
        {
            var (orderId, quoteId, contractId) = await CreateUploadedContractAsync(custToken, adminToken);
            if (contractId == null) return (orderId, quoteId, null);

            var salesToken = _ctx.GetToken("Sales") ?? adminToken;
            var verReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contracts/{contractId}/verify");
            verReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
            var res = await _http.SendAsync(verReq);
            if (res.IsSuccessStatusCode) return (orderId, quoteId, contractId);
            if (_verbose)
                Console.WriteLine($"     prereq contract verify failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
        }
        catch { }
        return (null, null, null);
    }

    private async Task CreateRouteStopAndScheduleAsync(string adminToken)
    {
        try
        {
            var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);
            var routeCode = $"R-{Guid.NewGuid():N}"[..10];
            var routeReq = new HttpRequestMessage(HttpMethod.Post, "/api/routes");
            routeReq.Headers.Authorization = authHeader;
            routeReq.Content = new StringContent("{\"routeCode\":\"" + routeCode + "\",\"originCity\":\"Ho Chi Minh City\",\"destCity\":\"Can Tho\",\"transitTime\":\"6h\",\"status\":\"Active\"}", Encoding.UTF8, "application/json");
            var routeRes = await _http.SendAsync(routeReq);
            if (!routeRes.IsSuccessStatusCode) return;

            var routeBody = await routeRes.Content.ReadAsStringAsync();
            using var routeDoc = JsonDocument.Parse(routeBody);
            if (!routeDoc.RootElement.TryGetProperty("data", out var rData) || (!rData.TryGetProperty("routeId", out var rid) && !rData.TryGetProperty("id", out rid)))
                return;

            var routeId = rid.GetString()!;
            _ctx.Set("routeId", routeId);
            _ctx.Set("routeCode", routeCode);

            var tierReq = new HttpRequestMessage(HttpMethod.Post, "/api/weight-tiers");
            tierReq.Headers.Authorization = authHeader;
            tierReq.Content = new StringContent("{\"routeId\":\"" + routeId + "\",\"minWeightKg\":0,\"maxWeightKg\":1000,\"pricePerKg\":12000}", Encoding.UTF8, "application/json");
            await _http.SendAsync(tierReq);

            var stopReq = new HttpRequestMessage(HttpMethod.Post, $"/api/routes/{routeId}/stops");
            stopReq.Headers.Authorization = authHeader;
            stopReq.Content = new StringContent("{\"stopName\":\"Tram Thu Phi Long An\",\"durationMinutes\":30,\"sequence\":1}", Encoding.UTF8, "application/json");
            var stopRes = await _http.SendAsync(stopReq);
            if (stopRes.IsSuccessStatusCode)
            {
                var body = await stopRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && (data.TryGetProperty("stopId", out var stid) || data.TryGetProperty("id", out stid)))
                {
                    var sId = stid.GetString()!;
                    _ctx.Set("stopId", sId);
                    _ctx.Set("dropoffStopId", sId);
                }
            }

            var schedReq = new HttpRequestMessage(HttpMethod.Post, $"/api/routes/{routeId}/schedules");
            schedReq.Headers.Authorization = authHeader;
            schedReq.Content = new StringContent("{\"departureDate\":\"" + DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd") + "\",\"departureTime\":\"08:00:00\",\"cutOffTime\":\"06:00:00\",\"status\":\"ACTIVE\"}", Encoding.UTF8, "application/json");
            var schedRes = await _http.SendAsync(schedReq);
            if (schedRes.IsSuccessStatusCode)
            {
                var body = await schedRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && (data.TryGetProperty("scheduleId", out var scid) || data.TryGetProperty("id", out scid)))
                {
                    _ctx.Set("scheduleId", scid.GetString()!);
                }
            }
        }
        catch { }
    }

    private async Task<string?> CreateDraftAppendixAsync(string custToken, string adminToken)
    {
        try
        {
            await CreateInboundQcAsync(custToken, adminToken, true);
            var ordId = _ctx.Get("orderId");
            if (ordId == null) return null;

            var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/appendices/by-order/{ordId}");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var getRes = await _http.SendAsync(getReq);
            if (getRes.IsSuccessStatusCode)
            {
                var body = await getRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && (data.TryGetProperty("appendixId", out var aid) || data.TryGetProperty("id", out aid)))
                {
                    string appendixId = aid.GetString()!;
                    _ctx.Set("appendixId", appendixId);
                    return appendixId;
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> CreateSentAppendixAsync(string custToken, string adminToken)
    {
        try
        {
            var appendixId = await CreateDraftAppendixAsync(custToken, adminToken);
            if (appendixId == null) return null;

            var sendReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contracts/appendices/{appendixId}/send");
            sendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var res = await _http.SendAsync(sendReq);
            if (res.IsSuccessStatusCode) return appendixId;
        }
        catch { }
        return null;
    }

    private async Task CreateAcceptedAppendixAsync(string custToken, string adminToken)
    {
        try
        {
            var appendixId = await CreateSentAppendixAsync(custToken, adminToken);
            if (appendixId == null) return;

            var accReq = new HttpRequestMessage(HttpMethod.Post, $"/api/contracts/appendices/{appendixId}/accept");
            accReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            await _http.SendAsync(accReq);
        }
        catch { }
    }

    private async Task<(string? orderId, string? asnId)> CreateAsnAsync(string custToken, string adminToken)
    {
        try
        {
            var (orderId, _, _) = await CreateSignedContractAsync(custToken, adminToken);
            if (orderId == null) return (null, null);
            _ctx.Set("orderId", orderId);

            var asnReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/asns");
            asnReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            var whId = _ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001";
            asnReq.Content = new StringContent("{\"orderId\":\"" + orderId + "\",\"requestedDropoffTime\":\"" + DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",\"warehouseId\":\"" + whId + "\",\"phone\":\"0901234567\"}", Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(asnReq);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.TryGetProperty("data", out var payload) ? payload : doc.RootElement;
                var asnId = GetStringProperty(data, "asnId", "AsnId", "id", "Id");
                if (!string.IsNullOrWhiteSpace(asnId))
                {
                    _ctx.Set("asnId", asnId);
                    return (orderId, asnId);
                }
            }
            else if (_verbose)
            {
                Console.WriteLine($"     prereq ASN failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
            }
        }
        catch { }
        return (null, null);
    }

    private async Task CreateInboundQcAsync(string custToken, string adminToken, bool isDiscrepancy)
    {
        try
        {
            var (_, asnId) = await CreateAsnAsync(custToken, adminToken);
            if (asnId == null)
            {
                if (_verbose) Console.WriteLine("     prereq QC skipped: asnId was not created");
                return;
            }

            var qcReq = new HttpRequestMessage(HttpMethod.Post, "/api/inbound/qc");
            qcReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ctx.Get("warehouseworkerToken") ?? adminToken);
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(asnId), "AsnId");
            form.Add(new StringContent(isDiscrepancy ? "31" : "10"), "ActualWeightKg");
            form.Add(new StringContent(isDiscrepancy ? "20" : "21.54"), "LengthCm");
            form.Add(new StringContent(isDiscrepancy ? "20" : "21.54"), "WidthCm");
            form.Add(new StringContent(isDiscrepancy ? "20" : "21.56"), "HeightCm");
            form.Add(new StringContent("-15"), "Temperature");
            form.Add(new StringContent(_ctx.Get("warehouseId") ?? "73000000-0000-0000-0000-000000000001"), "WarehouseId");
            qcReq.Content = form;

            var res = await _http.SendAsync(qcReq);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.TryGetProperty("data", out var payload) && payload.ValueKind == JsonValueKind.Object
                    ? payload
                    : doc.RootElement;
                if (data.ValueKind == JsonValueKind.Object)
                {
                    var lpnId = GetStringProperty(data, "lpnId", "LpnId", "id", "Id");
                    if (!string.IsNullOrWhiteSpace(lpnId))
                        _ctx.Set("lpnId", lpnId);
                    var lpnCode = GetStringProperty(data, "lpnCode", "LpnCode");
                    if (!string.IsNullOrWhiteSpace(lpnCode))
                        _ctx.Set("lpnCode", lpnCode);
                    var receiptId = GetStringProperty(data, "receiptId", "ReceiptId");
                    if (!string.IsNullOrWhiteSpace(receiptId))
                        _ctx.Set("receiptId", receiptId);
                }
                if (_verbose)
                    Console.WriteLine($"     prereq QC created: {Trunc(body, 600)}");

                if (!isDiscrepancy)
                    await GenerateReceiptAndPutawayAsync(adminToken);
            }
            else if (_verbose)
            {
                Console.WriteLine($"     prereq QC failed ({(int)res.StatusCode}): {Trunc(await res.Content.ReadAsStringAsync(), 160)}");
            }
        }
        catch { }
    }

    private async Task GenerateReceiptAndPutawayAsync(string adminToken)
    {
        var asnId = _ctx.Get("asnId");
        var lpnId = _ctx.Get("lpnId");
        var warehouseId = _ctx.Get("warehouseId");
        if (string.IsNullOrWhiteSpace(asnId) ||
            string.IsNullOrWhiteSpace(lpnId) ||
            string.IsNullOrWhiteSpace(warehouseId))
            return;

        var authHeader = new AuthenticationHeaderValue("Bearer", adminToken);

        var receiptReq = new HttpRequestMessage(HttpMethod.Post, "/api/inbound/receipts/generate");
        receiptReq.Headers.Authorization = authHeader;
        receiptReq.Content = new StringContent(JsonSerializer.Serialize(new
        {
            asnId,
            delivererName = "Test Runner Driver",
            vehiclePlate = _ctx.Get("truckPlate") ?? "51C-TEST",
            note = "Generated by test runner prerequisite flow"
        }), Encoding.UTF8, "application/json");
        var receiptRes = await _http.SendAsync(receiptReq);
        if (!receiptRes.IsSuccessStatusCode && _verbose)
            Console.WriteLine($"     prereq receipt failed ({(int)receiptRes.StatusCode}): {Trunc(await receiptRes.Content.ReadAsStringAsync(), 160)}");

        var putawayReq = new HttpRequestMessage(HttpMethod.Post, "/api/inbound/putaway");
        putawayReq.Headers.Authorization = authHeader;
        putawayReq.Content = new StringContent(JsonSerializer.Serialize(new
        {
            lpnId,
            warehouseId,
            storageLocation = $"TEST-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        }), Encoding.UTF8, "application/json");
        var putawayRes = await _http.SendAsync(putawayReq);
        if (!putawayRes.IsSuccessStatusCode && _verbose)
            Console.WriteLine($"     prereq putaway failed ({(int)putawayRes.StatusCode}): {Trunc(await putawayRes.Content.ReadAsStringAsync(), 160)}");
    }

    private async Task CreateOutboundOrderAsync(string custToken, string adminToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/outbound/orders");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = new StringContent("{\"customerId\":\"60000000-0000-0000-0000-000000000001\",\"receiverName\":\"Nguyen Van A\",\"receiverPhone\":\"0901234567\",\"destinationAddress\":\"456 Le Duan, Da Nang\"}", Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && (data.TryGetProperty("outboundOrderId", out var oid) || data.TryGetProperty("id", out oid)))
                {
                    var id = oid.GetString();
                    if (id != null)
                    {
                        _ctx.Set("outboundOrderId", id);
                        _ctx.Set("outboundId", id);
                    }
                }
            }
        }
        catch { }
    }

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}

public class LoginCredential
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
