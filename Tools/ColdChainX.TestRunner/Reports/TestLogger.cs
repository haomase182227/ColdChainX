using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Reports;

public class TestLogger : IDisposable
{
    private readonly StreamWriter _writer;
    public string LogFilePath { get; }

    public TestLogger(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        LogFilePath = Path.Combine(outputDir, $"run_{timestamp}.log");
        _writer = new StreamWriter(LogFilePath, false, System.Text.Encoding.UTF8) { AutoFlush = true };

        _writer.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        _writer.WriteLine($"║       ColdChainX TestRunner — Execution Log                 ║");
        _writer.WriteLine($"║       Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}                         ║");
        _writer.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
        _writer.WriteLine();
    }

    public void LogInfo(string message)
    {
        _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] INFO  {message}");
    }

    public void LogWarn(string message)
    {
        _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] WARN  {message}");
    }

    public void LogError(string message, Exception? ex = null)
    {
        _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ERROR {message}");
        if (ex != null)
        {
            _writer.WriteLine($"  Exception Type: {ex.GetType().Name}");
            _writer.WriteLine($"  Message: {ex.Message}");
            if (ex.StackTrace != null)
                _writer.WriteLine($"  Stack: {ex.StackTrace}");
        }
    }

    public void LogRequest(string functionCode, string testCaseId, string method, string url, string? body = null)
    {
        _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ──── REQUEST {functionCode}.{testCaseId} ────");
        _writer.WriteLine($"  {method} {url}");
        if (!string.IsNullOrEmpty(body))
            _writer.WriteLine($"  Body: {Truncate(body, 500)}");
    }

    public void LogResponse(string functionCode, string testCaseId, int statusCode, string body, long elapsedMs)
    {
        _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ──── RESPONSE {functionCode}.{testCaseId} ({elapsedMs}ms) ────");
        _writer.WriteLine($"  HTTP {statusCode}");
        _writer.WriteLine($"  Body: {Truncate(body, 1000)}");
    }

    public void LogTestResult(TestResult result)
    {
        var icon = result.Status switch
        {
            TestStatus.Passed => "✓",
            TestStatus.Failed => "✗",
            TestStatus.Skipped => "⊘",
            _ => "?"
        };
        _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {icon} {result.FunctionCode}.{result.TestCaseId} [{result.TestCaseType}] → {result.Status}: {result.Message}");
        if (result.Status == TestStatus.Failed && result.ResponseBody != null)
            _writer.WriteLine($"  Failed Response: {Truncate(result.ResponseBody, 500)}");
        _writer.WriteLine($"  Matched → Return: [{string.Join(",", result.MatchedReturnIndices)}] Exception: {result.MatchedExceptionIndex} Log: [{string.Join(",", result.MatchedLogIndices)}]");
    }

    public void LogSummary(List<TestResult> results)
    {
        var passed = results.Count(r => r.Status == TestStatus.Passed);
        var failed = results.Count(r => r.Status == TestStatus.Failed);
        var skipped = results.Count(r => r.Status == TestStatus.Skipped);
        var total = results.Count;
        var rate = total > 0 ? (passed * 100.0 / total).ToString("F1") : "0";

        _writer.WriteLine();
        _writer.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        _writer.WriteLine($"║  SUMMARY                                                    ║");
        _writer.WriteLine($"║  Total: {total,-6} Passed: {passed,-6} Failed: {failed,-6} Skipped: {skipped,-4}  ║");
        _writer.WriteLine($"║  Pass Rate: {rate}%                                           ║");
        _writer.WriteLine($"║  Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}                         ║");
        _writer.WriteLine($"╚══════════════════════════════════════════════════════════════╝");

        if (failed > 0)
        {
            _writer.WriteLine();
            _writer.WriteLine($"── {failed} FAILED TEST(S) ──");
            foreach (var f in results.Where(r => r.Status == TestStatus.Failed))
            {
                _writer.WriteLine($"  ✗ {f.FunctionCode}.{f.TestCaseId} [{f.TestCaseDesc}]");
                _writer.WriteLine($"    HTTP {f.HttpStatusCode}: {f.Message}");
                if (f.ResponseBody != null)
                    _writer.WriteLine($"    Body: {Truncate(f.ResponseBody, 300)}");
            }
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }
}
