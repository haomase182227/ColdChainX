using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using ColdChainX.TestRunner.Core;
using ColdChainX.TestRunner.Models;
using ColdChainX.TestRunner.Reports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


bool isCliMode = args.Length > 0 && args.Any(a => a.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || 
                               a == "--dry-run" || a == "--stop-on-fail" || a == "--filter" || a == "-f" || a == "--base-url" || a == "--quiet" || a == "-v" || a == "--verbose");

if (isCliMode)
{
    return await RunCliModeAsync(args);
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine(@"
╔══════════════════════════════════════════════════════════╗
║       ❄  ColdChainX API Test Runner  ❄                 ║
║       Web Dashboard & Automated Spec Executor            ║
╚══════════════════════════════════════════════════════════╝
");
Console.ResetColor();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options => { options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()); });
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 128 * 1024 * 1024;
    options.ValueLengthLimit = 128 * 1024 * 1024;
    options.MultipartHeadersLengthLimit = 128 * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 128 * 1024 * 1024;
});
builder.Services.AddSingleton<TestRunState>();

var app = builder.Build();
app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();


app.MapGet("/api/spec/auto-detect", () =>
{
    var found = ProgramHelpers.FindCandidateSpecFiles();
    var best = found.FirstOrDefault(f => f.Contains("101_to_212", StringComparison.OrdinalIgnoreCase))
            ?? found.FirstOrDefault()
            ?? @"C:\Users\ASUS\Music\CN 9\ĐA\SP26SE002_Unit_Test_ColdChainX_101_to_212_Functions_Spec.html";
    return Results.Ok(new { path = best, exists = File.Exists(best), candidates = found });
});

app.MapGet("/api/spec/candidates", () =>
{
    var list = ProgramHelpers.FindCandidateSpecFiles().Select(p => new
    {
        path = p,
        name = Path.GetFileName(p),
        dir = Path.GetDirectoryName(p),
        size = new FileInfo(p).Length,
        lastModified = new FileInfo(p).LastWriteTime.ToString("yyyy-MM-dd HH:mm")
    }).ToList();

    return Results.Ok(list);
});

app.MapPost("/api/spec/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType || request.Form.Files.Count == 0)
        return Results.BadRequest(new { error = "Không có file nào được tải lên." });

    var file = request.Form.Files[0];
    if (!file.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Vui lòng chọn file có định dạng .html" });

    var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "UploadedSpecs");
    if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

    var savePath = Path.Combine(uploadDir, file.FileName);
    using (var stream = new FileStream(savePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    try
    {
        var specs = HtmlSpecParser.Parse(savePath);
        return Results.Ok(new
        {
            success = true,
            path = savePath,
            name = file.FileName,
            totalFunctions = specs.Count,
            totalTestCases = specs.Sum(s => s.TestCases.Count)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = "File tải lên thành công nhưng không phân tích được dữ liệu kiểm thử: " + ex.Message, path = savePath });
    }
});

app.MapGet("/api/spec/browse", (string? currentPath) =>
{
    string? selectedPath = null;
    bool cancelled = true;

    var thread = new Thread(() =>
    {
        try
        {
            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Chọn file Đặc tả Kiểm thử (ColdChainX HTML Spec)",
                Filter = "HTML Spec Files (*.html)|*.html|All Files (*.*)|*.*",
                InitialDirectory = !string.IsNullOrEmpty(currentPath) && Directory.Exists(Path.GetDirectoryName(currentPath))
                    ? Path.GetDirectoryName(currentPath)!
                    : @"C:\Users\ASUS\Music\CN 9\ĐA"
            };

            using var form = new System.Windows.Forms.Form
            {
                TopMost = true,
                TopLevel = true,
                ShowInTaskbar = false,
                WindowState = System.Windows.Forms.FormWindowState.Minimized
            };
            form.Show();
            form.BringToFront();
            form.Activate();

            if (dlg.ShowDialog(form) == System.Windows.Forms.DialogResult.OK)
            {
                selectedPath = dlg.FileName;
                cancelled = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BrowseDialog Error] {ex.Message}");
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(60000))
    {
        try { thread.Interrupt(); } catch { }
    }

    if (cancelled || selectedPath == null)
        return Results.Ok(new { cancelled = true, path = currentPath ?? "" });

    return Results.Ok(new { cancelled = false, path = selectedPath });
});

app.MapGet("/api/spec/parse", (string? path) =>
{
    if (string.IsNullOrEmpty(path) || !File.Exists(path))
        return Results.BadRequest(new { error = "Không tìm thấy file Spec HTML tại đường dẫn đã cho." });

    try
    {
        var specs = HtmlSpecParser.Parse(path);
        var totalTcs = specs.Sum(s => s.TestCases.Count);
        return Results.Ok(new
        {
            success = true,
            totalFunctions = specs.Count,
            totalTestCases = totalTcs,
            functions = specs.Select(s => new { s.Code, s.ClsName, s.FuncName, tcCount = s.TestCases.Count }).ToList()
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/run/status", (TestRunState state) =>
{
    return Results.Ok(state.GetSnapshot());
});

app.MapPost("/api/run/stop", (TestRunState state) =>
{
    state.StopRun();
    return Results.Ok(new { message = "Đã gửi lệnh dừng test." });
});

app.MapPost("/api/actions/open-result", (TestRunState state) =>
{
    var path = state.LastResultHtmlPath;
    if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(state.LastSpecFilePath))
    {
        var dir = Path.GetDirectoryName(state.LastSpecFilePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(state.LastSpecFilePath);
        path = Path.Combine(dir, $"{name}_Result.html");
    }

    if (string.IsNullOrEmpty(path) || !File.Exists(path))
        return Results.BadRequest(new { error = "Không tìm thấy file kết quả. Bạn cần chạy test xong ít nhất 1 lần để xuất kết quả!" });

    try
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Results.Ok(new { message = "Đã mở file trên trình duyệt." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/actions/open-logs", (TestRunState state) =>
{
    var folder = string.IsNullOrEmpty(state.LastLogFilePath) 
        ? Path.Combine(Directory.GetCurrentDirectory(), "TestRunner_Results")
        : Path.GetDirectoryName(state.LastLogFilePath);

    if (folder == null || !Directory.Exists(folder))
        Directory.CreateDirectory(folder ?? "TestRunner_Results");

    try
    {
        Process.Start("explorer.exe", folder ?? ".");
        return Results.Ok(new { message = "Đã mở thư mục logs." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/run/start", (StartRunRequest req, TestRunState state, IConfiguration config) =>
{
    if (state.IsRunning)
        return Results.BadRequest(new { error = "Đang có một đợt test đang chạy. Vui lòng dừng hoặc chờ hoàn thiện." });

    if (string.IsNullOrEmpty(req.SpecPath) || !File.Exists(req.SpecPath))
        return Results.BadRequest(new { error = "File Spec HTML không tồn tại!" });

    _ = Task.Run(async () =>
    {
        try
        {
            var logDir = Path.Combine(Path.GetDirectoryName(req.SpecPath) ?? ".", "TestRunner_Results");
            using var logger = new TestLogger(logDir);
            logger.LogInfo($"Start test run via Web UI. Spec: {req.SpecPath}, URL: {req.BaseUrl}");

            var specs = HtmlSpecParser.Parse(req.SpecPath);
            var filtered = string.IsNullOrEmpty(req.FilterModule) 
                ? specs 
                : specs.Where(s => s.Code.StartsWith(req.FilterModule, StringComparison.OrdinalIgnoreCase)).ToList();
            var totalTcs = filtered.Sum(s => s.TestCases.Count);

            state.StartRun(totalTcs, req.SpecPath);
            state.LastLogFilePath = logger.LogFilePath;

            if (req.DryRun)
            {
                state.UpdateStatus("⚡ Đang kiểm tra cú pháp (Dry Run)...");
                foreach (var spec in filtered)
                {
                    if (state.Cts?.IsCancellationRequested == true) break;
                    foreach (var tc in spec.TestCases)
                    {
                        await Task.Delay(10); // Tạo hiệu ứng chạy qua từng ca test
                        var res = TestResult.Skipped(spec.Code, tc.Id, tc.Type, tc.Desc, "Dry Run (Kiểm tra cú pháp OK)");
                        state.AddResult(res);
                    }
                }
                state.CompleteRun("🏁 Hoàn tất chế độ Dry Run (Không gửi request tới backend)!");
                return;
            }

            state.UpdateStatus("🔌 Đang kiểm tra kết nối với Backend Server...");
            using (var checkClient = new HttpClient { BaseAddress = new Uri(req.BaseUrl), Timeout = TimeSpan.FromSeconds(5) })
            {
                try
                {
                    await checkClient.GetAsync("/api/auth/roles", state.Cts?.Token ?? default);
                }
                catch (Exception ex)
                {
                    logger.LogError("Backend offline", ex);
                    state.CompleteRun($"❌ Lỗi kết nối Backend tại {req.BaseUrl}. Hãy đảm bảo server API đang chạy!");
                    return;
                }
            }

            var mapPath = Path.Combine(AppContext.BaseDirectory, "endpoint_map.json");
            if (!File.Exists(mapPath)) mapPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoint_map.json");
            var mapper = EndpointMapper.LoadFromFile(mapPath);

            var ctx = new TestContext(verbose: false);
            var runner = new TestRunner(req.BaseUrl, mapper, ctx, verbose: false, stopOnFail: req.StopOnFail);

            state.UpdateStatus("🔐 Đang đăng nhập lấy Token...");
            var credentials = new Dictionary<string, LoginCredential>();
            var credSection = config.GetSection("TestCredentials");
            foreach (var child in credSection.GetChildren())
            {
                credentials[child.Key] = new LoginCredential { Email = child["Email"] ?? "", Password = child["Password"] ?? "" };
            }
            if (credentials.Count == 0)
                credentials["Admin"] = new LoginCredential { Email = "admin01@coldchainx.com", Password = "Password@123" };

            await runner.BootstrapAsync(credentials);

            state.UpdateStatus("🚀 Bắt đầu thực thi kiểm thử...");
            var results = await runner.RunAllAsync(filtered, req.FilterModule, res =>
            {
                state.AddResult(res);
                logger.LogTestResult(res);
            }, state.Cts?.Token ?? default);

            logger.LogSummary(results);

            state.UpdateStatus("📝 Đang xuất kết quả (dấu O) vào file HTML...");
            try
            {
                var resultPath = HtmlResultExporter.Export(req.SpecPath, specs, state.GetAllResults());
                var jsonPath = JsonResultExporter.Export(req.SpecPath, state.GetAllResults());
                state.LastResultHtmlPath = resultPath;
                logger.LogInfo($"Exported HTML: {resultPath}");
                logger.LogInfo($"Exported JSON Report: {jsonPath}");
                state.CompleteRun();
            }
            catch (Exception ex)
            {
                logger.LogError("HTML export fail", ex);
                state.CompleteRun($"⚠ Chạy xong nhưng xuất HTML lỗi: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            state.CompleteRun($"❌ Đã xảy ra lỗi: {ex.Message}");
        }
    });

    return Results.Ok(new { message = "Đã khởi chạy kiểm thử ngầm thành công." });
});

Console.WriteLine("🚀 Web Server khởi chạy! Vui lòng truy cập trình duyệt tại địa chỉ http://localhost:5288");
app.Run();
return 0;


static async Task<int> RunCliModeAsync(string[] cliArgs)
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"
╔══════════════════════════════════════════════════════════╗
║       ❄  ColdChainX API Test Runner (CLI Mode) ❄       ║
╚══════════════════════════════════════════════════════════╝
");
    Console.ResetColor();

    string? htmlPath = null;
    string? filterModule = null;
    string? baseUrlOverride = null;
    bool verbose = true;
    bool stopOnFail = false;
    bool dryRun = false;

    for (int i = 0; i < cliArgs.Length; i++)
    {
        if ((cliArgs[i] == "--filter" || cliArgs[i] == "-f") && i + 1 < cliArgs.Length)
            filterModule = cliArgs[++i];
        else if (cliArgs[i] == "--base-url" && i + 1 < cliArgs.Length)
            baseUrlOverride = cliArgs[++i];
        else if (cliArgs[i] == "--quiet" || cliArgs[i] == "-q")
            verbose = false;
        else if (cliArgs[i] == "--verbose" || cliArgs[i] == "-v")
            verbose = true;
        else if (cliArgs[i] == "--stop-on-fail")
            stopOnFail = true;
        else if (cliArgs[i] == "--dry-run")
            dryRun = true;
        else if (!cliArgs[i].StartsWith("-"))
            htmlPath = cliArgs[i];
    }

    if (string.IsNullOrEmpty(htmlPath) || !File.Exists(htmlPath))
    {
        var candidates = ProgramHelpers.FindCandidateSpecFiles();
        htmlPath = candidates.FirstOrDefault() ?? @"C:\Users\ASUS\Music\CN 9\ĐA\SP26SE002_Unit_Test_ColdChainX_100_Functions_Spec.html";
    }

    htmlPath = Path.GetFullPath(htmlPath);
    var logDir = Path.Combine(Path.GetDirectoryName(htmlPath) ?? ".", "TestRunner_Results");
    using var logger = new TestLogger(logDir);

    var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(configPath)) configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

    var config = new ConfigurationBuilder().SetBasePath(Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: true).Build();
    var baseUrl = baseUrlOverride ?? config["BaseUrl"] ?? "http://localhost:5244";
    Console.WriteLine($"🌐 API Base URL: {baseUrl}");

    Console.Write($"📄 Parsing HTML spec: {Path.GetFileName(htmlPath)}... ");
    List<TestSpec> specs;
    try { specs = HtmlSpecParser.Parse(htmlPath); }
    catch (Exception ex) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"FAILED: {ex.Message}"); return 1; }
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"OK ({specs.Count} functions, {specs.Sum(s => s.TestCases.Count)} test cases)");
    Console.ResetColor();

    if (dryRun)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n🏁 Dry run complete. No tests executed.");
        Console.ResetColor();
        foreach (var s in specs) Console.WriteLine($"  [{s.Code}] {s.ClsName}.{s.FuncName} — {s.TestCases.Count} TCs");
        return 0;
    }

    Console.Write("🔌 Checking server connection... ");
    try
    {
        using var checkClient = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
        var checkResp = await checkClient.GetAsync("/api/auth/roles");
        Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"OK ({(int)checkResp.StatusCode})"); Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"FAILED: {ex.Message}\n   ⚠ Make sure backend is running: dotnet run --project ColdChainX.API"); return 1;
    }

    var mapPath = Path.Combine(AppContext.BaseDirectory, "endpoint_map.json");
    if (!File.Exists(mapPath)) mapPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoint_map.json");
    var mapper = EndpointMapper.LoadFromFile(mapPath);

    var ctx = new TestContext(verbose);
    var runner = new TestRunner(baseUrl, mapper, ctx, verbose, stopOnFail);

    var credentials = new Dictionary<string, LoginCredential>();
    var credSection = config.GetSection("TestCredentials");
    foreach (var child in credSection.GetChildren())
    {
        credentials[child.Key] = new LoginCredential { Email = child["Email"] ?? "", Password = child["Password"] ?? "" };
    }
    if (credentials.Count == 0) credentials["Admin"] = new LoginCredential { Email = "admin01@coldchainx.com", Password = "Password@123" };

    await runner.BootstrapAsync(credentials);
    var results = await runner.RunAllAsync(specs, filterModule);

    foreach (var r in results) logger.LogTestResult(r);
    logger.LogSummary(results);
    ConsoleReporter.Print(results);

    try
    {
        var resPath = HtmlResultExporter.Export(htmlPath, specs, results);
        var jsonPath = JsonResultExporter.Export(htmlPath, results);
        Console.WriteLine($"   📂 Result HTML file: {resPath}");
        Console.WriteLine($"   📊 Result JSON report: {jsonPath}");
    }
    catch (Exception ex) { Console.WriteLine($"FAILED export: {ex.Message}"); }

    return results.Any(r => r.Status == TestStatus.Failed) ? 1 : 0;
}

public class StartRunRequest
{
    public string SpecPath { get; set; } = "";
    public string BaseUrl { get; set; } = "http://localhost:5244";
    public string? FilterModule { get; set; }
    public bool StopOnFail { get; set; }
    public bool DryRun { get; set; }
}

public static partial class ProgramHelpers
{
    public static List<string> FindCandidateSpecFiles()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Users\ASUS\Music\CN 9\ĐA\SP26SE002_Unit_Test_ColdChainX_101_to_212_Functions_Spec.html",
            @"C:\Users\ASUS\Music\CN 9\ĐA\SP26SE002_Unit_Test_ColdChainX_212_Functions_Spec.html",
            @"C:\Users\ASUS\Music\CN 9\ĐA\SP26SE002_Unit_Test_ColdChainX_100_Functions_Spec.html",
            @"C:\Users\ASUS\Music\CN 9\ĐA\Unit test\SP26SE002_Unit_Test_ColdChainX_101_to_212_Functions_Spec.html",
            @"C:\Users\ASUS\Music\CN 9\ĐA\Unit test\SP26SE002_Unit_Test_ColdChainX_100_Functions_Spec.html",
            Path.Combine(Directory.GetCurrentDirectory(), "SP26SE002_Unit_Test_ColdChainX_101_to_212_Functions_Spec.html"),
            Path.Combine(AppContext.BaseDirectory, "SP26SE002_Unit_Test_ColdChainX_101_to_212_Functions_Spec.html")
        };

        var curr = new DirectoryInfo(Directory.GetCurrentDirectory());
        int levels = 0;
        while (curr != null && levels < 6)
        {
            try
            {
                foreach (var f in curr.GetFiles("*Spec*.html", SearchOption.TopDirectoryOnly))
                {
                    candidates.Add(f.FullName);
                }
                foreach (var f in curr.GetFiles("SP26SE002*.html", SearchOption.TopDirectoryOnly))
                {
                    candidates.Add(f.FullName);
                }

                var unitTestDir = Path.Combine(curr.FullName, "Unit test");
                if (Directory.Exists(unitTestDir))
                {
                    foreach (var f in Directory.GetFiles(unitTestDir, "*.html"))
                    {
                        candidates.Add(f);
                    }
                }
            }
            catch { }

            curr = curr.Parent;
            levels++;
        }

        return candidates.Where(File.Exists).ToList();
    }
}
