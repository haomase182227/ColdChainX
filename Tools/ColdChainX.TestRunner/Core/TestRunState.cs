using System.Collections.Concurrent;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Singleton service managing live execution state for Web UI real-time monitoring.
/// </summary>
public class TestRunState
{
    private readonly object _lock = new();
    private readonly List<TestResult> _results = new();

    public bool IsRunning { get; private set; }
    public string StatusText { get; private set; } = "Sẵn sàng (Ready)";
    public int TotalTestCases { get; private set; }
    public int CompletedCount { get; private set; }
    public int PassedCount { get; private set; }
    public int FailedCount { get; private set; }
    public int SkippedCount { get; private set; }

    public string? LastSpecFilePath { get; set; }
    public string? LastResultHtmlPath { get; set; }
    public string? LastLogFilePath { get; set; }

    public CancellationTokenSource? Cts { get; private set; }

    public void StartRun(int totalTestCases, string specPath)
    {
        lock (_lock)
        {
            IsRunning = true;
            StatusText = "Đang khởi tạo & đăng nhập (Bootstrapping Auth)...";
            TotalTestCases = totalTestCases;
            CompletedCount = 0;
            PassedCount = 0;
            FailedCount = 0;
            SkippedCount = 0;
            _results.Clear();
            LastSpecFilePath = specPath;
            LastResultHtmlPath = null;
            LastLogFilePath = null;
            Cts = new CancellationTokenSource();
        }
    }

    public void UpdateStatus(string status)
    {
        lock (_lock)
        {
            StatusText = status;
        }
    }

    public void AddResult(TestResult result)
    {
        lock (_lock)
        {
            _results.Add(result);
            CompletedCount++;
            if (result.Status == TestStatus.Passed) PassedCount++;
            else if (result.Status == TestStatus.Failed) FailedCount++;
            else if (result.Status == TestStatus.Skipped) SkippedCount++;
            StatusText = $"Đang chạy: {result.FunctionCode} [{CompletedCount}/{TotalTestCases}]...";
        }
    }

    public void CompleteRun(string? statusText = null)
    {
        lock (_lock)
        {
            IsRunning = false;
            if (statusText != null)
                StatusText = statusText;
            else
                StatusText = $"Hoàn tất! (Pass Rate: {(TotalTestCases > 0 ? (PassedCount * 100.0 / TotalTestCases).ToString("F1") : "0")}%)";
            Cts?.Dispose();
            Cts = null;
        }
    }

    public void StopRun()
    {
        lock (_lock)
        {
            if (IsRunning && Cts != null)
            {
                StatusText = "Đang dừng (Cancelling)...";
                Cts.Cancel();
            }
        }
    }

    public object GetSnapshot()
    {
        lock (_lock)
        {
            return new
            {
                IsRunning,
                StatusText,
                TotalTestCases,
                CompletedCount,
                PassedCount,
                FailedCount,
                SkippedCount,
                ProgressPercent = TotalTestCases > 0 ? (int)(CompletedCount * 100.0 / TotalTestCases) : 0,
                PassRate = TotalTestCases > 0 ? (PassedCount * 100.0 / TotalTestCases).ToString("F1") : "0.0",
                LastSpecFilePath,
                LastResultHtmlPath,
                LastLogFilePath,
                RecentResults = _results.TakeLast(50).Select(r => new
                {
                    r.FunctionCode,
                    r.TestCaseId,
                    r.TestCaseType,
                    r.TestCaseDesc,
                    Status = r.Status.ToString(),
                    r.HttpStatusCode,
                    r.Message,
                    r.ElapsedMs,
                    r.ResponseBody
                }).ToList(),
                AllResultsCount = _results.Count
            };
        }
    }

    public List<TestResult> GetAllResults()
    {
        lock (_lock)
        {
            return new List<TestResult>(_results);
        }
    }
}
