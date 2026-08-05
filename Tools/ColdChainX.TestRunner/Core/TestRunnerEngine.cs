using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Main test orchestrator. Runs all test cases sequentially with chaining.
/// </summary>
public class TestRunner
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly EndpointMapper _mapper;
    private readonly TestContext _ctx;
    private readonly bool _verbose;
    private readonly bool _stopOnFail;

    public TestRunner(string baseUrl, EndpointMapper mapper, TestContext ctx,
        bool verbose = true, bool stopOnFail = false)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _mapper = mapper;
        _ctx = ctx;
        _verbose = verbose;
        _stopOnFail = stopOnFail;
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Bootstrap: Login to get tokens for all roles</summary>
    public async Task BootstrapAsync(Dictionary<string, LoginCredential> credentials)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n🔐 Bootstrapping authentication...");
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
                    var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("accessToken", out var tok))
                        {
                            var token = tok.GetString()!;
                            _ctx.Set($"{role.ToLower()}Token", token);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"   ✓ {role} login OK");
                            Console.ResetColor();

                            // Also extract IDs
                            if (data.TryGetProperty("userId", out var uid))
                                _ctx.Set($"{role.ToLower()}UserId", uid.ToString());
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

        Console.WriteLine();
    }

    /// <summary>Run all test specs sequentially</summary>
    public async Task<List<TestResult>> RunAllAsync(List<TestSpec> specs, string? filterModule = null, Action<TestResult>? onTestCompleted = null, CancellationToken cancellationToken = default)
    {
        var results = new List<TestResult>();
        var filteredSpecs = specs.AsEnumerable();

        if (!string.IsNullOrEmpty(filterModule))
        {
            filteredSpecs = specs.Where(s =>
                s.Code.StartsWith(filterModule, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var spec in filteredSpecs)
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

            foreach (var tc in spec.TestCases)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("\n⚠ Test run cancelled by user.");
                    break;
                }

                var result = await RunTestCaseAsync(spec, tc, endpoint);
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

    private async Task<TestResult> RunTestCaseAsync(TestSpec spec, TestCaseSpec tc, EndpointInfo endpoint)
    {
        if (_verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [{tc.Id}] ({tc.Type}) {Trunc(tc.Desc, 60)}... ");
            Console.ResetColor();
        }

        // Determine auth token
        var token = ResolveToken(endpoint, tc, spec);

        try
        {
            // Build request
            var request = RequestBuilder.Build(endpoint, spec, tc, _ctx, token);

            // Log request URL
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine();
                Console.WriteLine($"     → {request.Method} {_baseUrl}{request.RequestUri}");
                Console.ResetColor();
            }

            // Send request
            var sw = Stopwatch.StartNew();
            var response = await _http.SendAsync(request);
            sw.Stop();

            var body = await response.Content.ReadAsStringAsync();

            // Extract values from successful responses for chaining
            if (response.IsSuccessStatusCode)
                ResponseExtractor.Extract(_ctx, spec.Code, body);

            // Validate
            var result = ResponseValidator.Validate(response, body, spec, tc, sw.ElapsedMilliseconds);

            // Log result
            if (_verbose)
            {
                var color = result.Status == TestStatus.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.ForegroundColor = color;
                Console.WriteLine($"     {result.Message} ({sw.ElapsedMilliseconds}ms)");
                Console.ResetColor();
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            var result = TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc,
                $"Connection error: {ex.Message}");

            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n     ✗ Connection error: {ex.Message}");
                Console.ResetColor();
            }

            return result;
        }
        catch (TaskCanceledException)
        {
            return TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc, "Request timed out (30s)");
        }
    }

    /// <summary>
    /// Resolve which auth token to use based on endpoint and test case preconditions.
    /// </summary>
    private string? ResolveToken(EndpointInfo endpoint, TestCaseSpec tc, TestSpec spec)
    {
        // Check if test case expects "No authentication" (unauthorized test)
        var activePreTexts = tc.Pre.Select(i => spec.Preconditions.ElementAtOrDefault(i) ?? "").ToList();
        var expectsNoAuth = activePreTexts.Any(p =>
            p.Contains("No authentication", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("token is invalid", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("No auth token", StringComparison.OrdinalIgnoreCase));

        if (expectsNoAuth)
            return null; // Intentionally send without token

        if (endpoint.AuthRole == "Anonymous")
            return null; // Endpoint doesn't need auth

        // Get token by role
        return _ctx.GetToken(endpoint.AuthRole);
    }

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}

public class LoginCredential
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
