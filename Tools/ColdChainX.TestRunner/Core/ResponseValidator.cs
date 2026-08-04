using System.Text.Json;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Validates API response against expected test case results.
/// Populates MatchedReturnIndices, MatchedExceptionIndex, MatchedLogIndices for HTML export.
/// </summary>
public static class ResponseValidator
{
    public static TestResult Validate(HttpResponseMessage response, string body,
        TestSpec spec, TestCaseSpec tc, long elapsedMs)
    {
        var statusCode = (int)response.StatusCode;

        // Try parse response body as JSON
        bool? apiSuccess = null;
        string? apiMessage = null;
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("success", out var s))
                apiSuccess = s.GetBoolean();
            if (doc.RootElement.TryGetProperty("message", out var m))
                apiMessage = m.GetString();
        }
        catch { /* not JSON, that's ok for some endpoints */ }

        // ── Match Return rows ──
        var matchedRet = MatchReturnIndices(spec.Returns, statusCode, apiMessage, body);

        // ── Match Exception rows ──
        var matchedExc = MatchExceptionIndex(spec.Exceptions, statusCode, apiMessage, body);

        // ── Match Log rows ──
        var matchedLog = MatchLogIndices(spec.Logs, statusCode, apiSuccess);

        // ── Determine pass/fail ──
        // Extract expected HTTP status code from test case's expected Returns
        int? expectedHttpStatus = null;
        foreach (var retIdx in tc.Ret)
        {
            var ret = spec.Returns.ElementAtOrDefault(retIdx) ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(ret, @"HTTP\s+(\d{3})");
            if (match.Success)
            {
                expectedHttpStatus = int.Parse(match.Groups[1].Value);
                break;
            }
        }

        // Determine if test expects success or error
        var expectedExcIdx = tc.Exc;
        var expectedExc = spec.Exceptions.ElementAtOrDefault(expectedExcIdx);
        var expectsSuccess = expectedExc == null || expectedExcIdx < 0;

        TestResult result;

        if (expectsSuccess)
        {
            // Case 1: Expect success
            if (statusCode >= 200 && statusCode < 300)
            {
                if (apiSuccess == true || apiSuccess == null)
                    result = TestResult.Passed(spec.Code, tc.Id, tc.Type, tc.Desc,
                        $"✓ {statusCode} OK: {Trunc(apiMessage ?? "success", 80)}", statusCode, elapsedMs);
                else
                    result = TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc,
                        $"HTTP {statusCode} but success=false: {Trunc(apiMessage, 80)}", statusCode, body, elapsedMs);
            }
            else
            {
                result = TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc,
                    $"Expected 2xx success but got HTTP {statusCode}: {Trunc(apiMessage ?? body, 100)}",
                    statusCode, body, elapsedMs);
            }
        }
        else
        {
            // Case 2: Expect failure/error
            if (expectedHttpStatus != null && statusCode != expectedHttpStatus)
            {
                if (statusCode >= 400)
                {
                    var excKeyword = ExtractExceptionKeyword(expectedExc!);
                    if (!string.IsNullOrEmpty(apiMessage) && ContainsKeyword(apiMessage, excKeyword))
                        result = TestResult.Passed(spec.Code, tc.Id, tc.Type, tc.Desc,
                            $"✓ {statusCode} (expected {expectedHttpStatus}): {Trunc(apiMessage, 80)}", statusCode, elapsedMs);
                    else
                        result = TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc,
                            $"Expected HTTP {expectedHttpStatus} but got {statusCode}: {Trunc(apiMessage ?? body, 80)}",
                            statusCode, body, elapsedMs);
                }
                else
                {
                    result = TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc,
                        $"Expected HTTP {expectedHttpStatus} error but got {statusCode}: {Trunc(apiMessage ?? body, 80)}",
                        statusCode, body, elapsedMs);
                }
            }
            else if (statusCode >= 400)
            {
                var excKeyword = ExtractExceptionKeyword(expectedExc!);
                if (!string.IsNullOrEmpty(excKeyword) && !string.IsNullOrEmpty(apiMessage)
                    && ContainsKeyword(apiMessage, excKeyword))
                    result = TestResult.Passed(spec.Code, tc.Id, tc.Type, tc.Desc,
                        $"✓ {statusCode}: {Trunc(apiMessage, 80)}", statusCode, elapsedMs);
                else
                    result = TestResult.Passed(spec.Code, tc.Id, tc.Type, tc.Desc,
                        $"✓ {statusCode} error: {Trunc(apiMessage ?? body, 80)}", statusCode, elapsedMs);
            }
            else
            {
                result = TestResult.Failed(spec.Code, tc.Id, tc.Type, tc.Desc,
                    $"Expected error ({expectedExc}) but got {statusCode} success: {Trunc(apiMessage ?? body, 80)}",
                    statusCode, body, elapsedMs);
            }
        }

        // ── Attach matched indices to result ──
        result.MatchedReturnIndices = matchedRet;
        result.MatchedExceptionIndex = matchedExc;
        result.MatchedLogIndices = matchedLog;

        return result;
    }

    /// <summary>Find which Return rows match the actual HTTP response</summary>
    private static List<int> MatchReturnIndices(List<string> returns, int statusCode, string? apiMessage, string body)
    {
        var matched = new List<int>();
        for (int i = 0; i < returns.Count; i++)
        {
            var ret = returns[i];
            // Check if Return text contains the actual HTTP status code
            var httpMatch = System.Text.RegularExpressions.Regex.Match(ret, @"HTTP\s+(\d{3})");
            if (httpMatch.Success)
            {
                var retStatus = int.Parse(httpMatch.Groups[1].Value);
                if (retStatus == statusCode)
                {
                    // Further check: if there's a keyword after the status code, verify it appears in the response
                    var colonIdx = ret.IndexOf('-');
                    if (colonIdx > 0)
                    {
                        var keyword = ret[(colonIdx + 1)..].Trim();
                        // Extract significant words (>3 chars)
                        var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(w => w.Length > 3).Take(3).ToList();
                        var bodyAndMsg = (apiMessage ?? "") + " " + body;
                        if (words.Count == 0 || words.Any(w => bodyAndMsg.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        {
                            matched.Add(i);
                        }
                    }
                    else
                    {
                        matched.Add(i);
                    }
                }
            }
        }
        // If nothing matched but we have returns, match first success or first error based on status
        if (matched.Count == 0 && returns.Count > 0)
        {
            if (statusCode >= 200 && statusCode < 300)
                matched.Add(0); // First return is typically the success case
        }
        return matched;
    }

    /// <summary>Find which Exception row matches the actual response</summary>
    private static int MatchExceptionIndex(List<string> exceptions, int statusCode, string? apiMessage, string body)
    {
        if (statusCode >= 200 && statusCode < 300)
            return -1; // Success — no exception

        var bodyAndMsg = (apiMessage ?? "") + " " + body;
        for (int i = 0; i < exceptions.Count; i++)
        {
            var exc = exceptions[i];
            var keyword = ExtractExceptionKeyword(exc);
            if (!string.IsNullOrEmpty(keyword) && ContainsKeyword(bodyAndMsg, keyword))
                return i;
        }
        // If error response but no specific match, return first exception
        if (exceptions.Count > 0 && statusCode >= 400)
            return 0;
        return -1;
    }

    /// <summary>Find which Log rows apply based on success/failure</summary>
    private static List<int> MatchLogIndices(List<string> logs, int statusCode, bool? apiSuccess)
    {
        if (logs.Count == 0) return new();

        var matched = new List<int>();
        bool isSuccess = statusCode >= 200 && statusCode < 300 && apiSuccess != false;

        // Convention: first log = success log, second log = error log
        if (isSuccess && logs.Count > 0)
            matched.Add(0);
        else if (!isSuccess && logs.Count > 1)
            matched.Add(1);
        else if (logs.Count > 0)
            matched.Add(0);

        return matched;
    }

    /// <summary>Extract keyword from exception like "ConflictException: Email already registered" → "Email already"</summary>
    private static string? ExtractExceptionKeyword(string exception)
    {
        var idx = exception.IndexOf(':');
        if (idx >= 0 && idx + 2 < exception.Length)
            return exception[(idx + 2)..].Trim();
        return exception;
    }

    private static bool ContainsKeyword(string message, string? keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return false;
        var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var significantWords = words.Where(w => w.Length > 3).Take(3).ToList();
        return significantWords.Count == 0 ||
               significantWords.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static string Trunc(string? s, int max)
        => s == null ? "" : s.Length <= max ? s : s[..max] + "...";
}

