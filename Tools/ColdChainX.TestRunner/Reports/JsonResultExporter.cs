using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Reports;

public static class JsonResultExporter
{
    public static string Export(string sourceSpecPath, List<TestResult> results, string? outputDir = null)
    {
        var dir = outputDir ?? Path.GetDirectoryName(sourceSpecPath) ?? ".";
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"TestResults_{timestamp}.json";
        var outputPath = Path.Combine(dir, fileName);

        var passed = results.Where(r => r.Status == TestStatus.Passed).ToList();
        var failed = results.Where(r => r.Status == TestStatus.Failed).ToList();
        var skipped = results.Where(r => r.Status == TestStatus.Skipped).ToList();

        var report = new
        {
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Summary = new
            {
                Total = results.Count,
                Passed = passed.Count,
                Failed = failed.Count,
                Skipped = skipped.Count,
                PassRate = results.Count > 0 ? $"{(double)passed.Count * 100.0 / results.Count:0.00}%" : "0%"
            },
            FailedTests = failed.Select(r => new
            {
                FunctionCode = r.FunctionCode,
                TestCaseId = r.TestCaseId,
                Type = r.TestCaseType,
                Description = r.TestCaseDesc,
                ExpectedResult = r.ExpectedResult,
                HttpStatusCode = r.HttpStatusCode,
                FailureReason = r.Message,
                BackendResponseBody = r.ResponseBody,
                ElapsedMs = r.ElapsedMs
            }),
            PassedTests = passed.Select(r => new
            {
                FunctionCode = r.FunctionCode,
                TestCaseId = r.TestCaseId,
                Type = r.TestCaseType,
                Description = r.TestCaseDesc,
                ExpectedResult = r.ExpectedResult,
                HttpStatusCode = r.HttpStatusCode,
                ElapsedMs = r.ElapsedMs
            }),
            SkippedTests = skipped.Select(r => new
            {
                FunctionCode = r.FunctionCode,
                TestCaseId = r.TestCaseId,
                Type = r.TestCaseType,
                Description = r.TestCaseDesc,
                ExpectedResult = r.ExpectedResult,
                Reason = r.Message
            })
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        File.WriteAllText(outputPath, json, Encoding.UTF8);

        try
        {
            var latestPath = Path.Combine(dir, "TestResults_Latest.json");
            File.WriteAllText(latestPath, json, Encoding.UTF8);
        }
        catch
        {
        }

        return outputPath;
    }
}
