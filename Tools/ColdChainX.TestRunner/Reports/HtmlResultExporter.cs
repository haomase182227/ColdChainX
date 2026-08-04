using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Reports;

/// <summary>
/// Exports test results back into the HTML spec file by injecting a testResults 
/// JavaScript variable. The HTML renderer reads testResults and renders O marks 
/// in the Confirm section (Return, Exception, Log message).
/// Also updates the stats row (Passed / Failed / Untested counts).
/// </summary>
public static class HtmlResultExporter
{
    /// <summary>
    /// Export test results into a new HTML file with O marks populated.
    /// </summary>
    /// <param name="sourceHtmlPath">Path to original HTML spec file</param>
    /// <param name="specs">Parsed test specs (same list used during execution)</param>
    /// <param name="results">All test results from the run</param>
    /// <param name="outputPath">Optional output path. If null, creates *_Result.html alongside source.</param>
    /// <returns>Path to the generated result HTML file</returns>
    public static string Export(string sourceHtmlPath, List<TestSpec> specs, List<TestResult> results, string? outputPath = null)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            var dir = Path.GetDirectoryName(sourceHtmlPath) ?? ".";
            var name = Path.GetFileNameWithoutExtension(sourceHtmlPath);
            outputPath = Path.Combine(dir, $"{name}_Result.html");
        }

        var html = File.ReadAllText(sourceHtmlPath, Encoding.UTF8);

        // ── Build testResults JSON object ──
        // Structure: { "AUTH001": { "UTCID01": { "status": "Passed", "httpCode": 200, "ret": [0], "exc": -1, "log": [0] }, ... }, ... }
        var resultsByFunc = results.GroupBy(r => r.FunctionCode);
        var testResultsObj = new Dictionary<string, Dictionary<string, object>>();

        foreach (var group in resultsByFunc)
        {
            var funcResults = new Dictionary<string, object>();
            foreach (var r in group)
            {
                funcResults[r.TestCaseId] = new Dictionary<string, object>
                {
                    ["status"] = r.Status.ToString(),
                    ["httpCode"] = r.HttpStatusCode,
                    ["ret"] = r.MatchedReturnIndices,
                    ["exc"] = r.MatchedExceptionIndex,
                    ["log"] = r.MatchedLogIndices,
                    ["ms"] = r.ElapsedMs,
                    ["msg"] = r.Message ?? ""
                };
            }
            testResultsObj[group.Key] = funcResults;
        }

        var testResultsJson = JsonSerializer.Serialize(testResultsObj, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // ── Inject testResults variable into HTML ──
        // Insert right before the existing `const specsData = [...]` line
        var injectionPoint = "const specsData =";
        var injectionIdx = html.IndexOf(injectionPoint, StringComparison.Ordinal);
        if (injectionIdx < 0)
            throw new InvalidDataException("Cannot find 'const specsData =' in HTML file for result injection.");

        var testResultsVar = $"const testResults = {testResultsJson};\n        ";
        html = html.Insert(injectionIdx, testResultsVar);

        // ── Modify the JavaScript rendering to use testResults ──
        // Replace empty tc-mark-cell in Confirm section with O marks from testResults
        html = InjectResultRendering(html);

        // ── Update stats row: replace Untested count with Passed/Failed ──
        html = InjectStatsRendering(html);

        File.WriteAllText(outputPath, html, Encoding.UTF8);
        return outputPath;
    }

    /// <summary>
    /// Modify the JavaScript in the HTML to render O marks from testResults
    /// in the Confirm section (Return, Exception, Log message).
    /// </summary>
    private static string InjectResultRendering(string html)
    {
        // The current HTML renders empty cells for Return, Exception, and Log:
        //   cells += `<td class="tc-mark-cell"></td>`;
        // We need to replace these in the Confirm section with logic that checks testResults.

        // Strategy: Replace the Confirm section builder entirely.
        // Find the comment: // Build Confirm Section
        var confirmStart = html.IndexOf("// Build Confirm Section", StringComparison.Ordinal);
        if (confirmStart < 0)
            return html; // Fallback: leave as-is if we can't find the section

        // Find the end of the Confirm section (next major comment or the sheet.innerHTML block)
        var confirmEnd = html.IndexOf("// Construct full sheet HTML", StringComparison.Ordinal);
        if (confirmEnd < 0)
            return html;

        var beforeConfirm = html[..confirmStart];
        var afterConfirm = html[confirmEnd..];

        // Build new Confirm section with result-aware rendering
        var newConfirmSection = @"// Build Confirm Section (Return, Exception, Log message) — with test results
            let confirmHtml = `
            <tr>
                <td class=""td-cat-navy"" rowspan=""${confirmRowSpan}"">Confirm</td>
                <td class=""section-divider"" colspan=""${1 + tcCount}"">Return</td>
            </tr>`;
            s.returns.forEach((item, idx) => {
                let cells = '';
                tcs.forEach(tc => {
                    let mark = '';
                    if (typeof testResults !== 'undefined' && testResults[s.code] && testResults[s.code][tc.id]) {
                        const tr = testResults[s.code][tc.id];
                        if (tr.ret && tr.ret.includes(idx)) mark = 'O';
                    }
                    cells += `<td class=""tc-mark-cell"">${mark}</td>`;
                });
                confirmHtml += `
                <tr>
                    <td class=""item-text-cell item-text-right"">${item}</td>
                    ${cells}
                </tr>`;
            });

            if (excCount > 0) {
                confirmHtml += `
                <tr>
                    <td class=""section-divider"" colspan=""${1 + tcCount}"">Exception</td>
                </tr>`;
                s.exceptions.forEach((item, idx) => {
                    let cells = '';
                    tcs.forEach(tc => {
                        let mark = '';
                        if (typeof testResults !== 'undefined' && testResults[s.code] && testResults[s.code][tc.id]) {
                            const tr = testResults[s.code][tc.id];
                            if (tr.exc === idx) mark = 'O';
                        }
                        cells += `<td class=""tc-mark-cell"">${mark}</td>`;
                    });
                    confirmHtml += `
                    <tr>
                        <td class=""item-text-cell item-text-right"">${item}</td>
                        ${cells}
                    </tr>`;
                });
            }

            if (logCount > 0) {
                confirmHtml += `
                <tr>
                    <td class=""section-divider"" colspan=""${1 + tcCount}"">Log message</td>
                </tr>`;
                s.logs.forEach((item, idx) => {
                    let cells = '';
                    tcs.forEach(tc => {
                        let mark = '';
                        if (typeof testResults !== 'undefined' && testResults[s.code] && testResults[s.code][tc.id]) {
                            const tr = testResults[s.code][tc.id];
                            if (tr.log && tr.log.includes(idx)) mark = 'O';
                        }
                        cells += `<td class=""tc-mark-cell"">${mark}</td>`;
                    });
                    confirmHtml += `
                    <tr>
                        <td class=""item-text-cell item-text-right"">${item}</td>
                        ${cells}
                    </tr>`;
                });
            }

            ";

        return beforeConfirm + newConfirmSection + afterConfirm;
    }

    /// <summary>
    /// Update the stats row to show actual Passed/Failed/Untested counts based on testResults.
    /// </summary>
    private static string InjectStatsRendering(string html)
    {
        // Replace the hardcoded stats row with dynamic rendering from testResults
        // Find: <td class="val-passed">0</td>
        //        <td>0</td>
        //        <td style="color: #d97706; font-weight: bold;">${tcCount}</td>
        // Replace with dynamic logic

        // We'll replace the stats row values section with JavaScript that reads testResults
        var oldPassedPattern = @"<td class=""val-passed"">0</td>";
        var newPassedHtml = @"<td class=""val-passed"">${(() => {
                    if (typeof testResults === 'undefined' || !testResults[s.code]) return 0;
                    const tr = testResults[s.code];
                    return Object.values(tr).filter(r => r.status === 'Passed').length;
                })()}</td>";
        html = html.Replace(oldPassedPattern, newPassedHtml);

        // Replace Failed count
        var oldFailedPattern = @"<td>0</td>
                            <td style=""color: #d97706; font-weight: bold;"">${tcCount}</td>";
        var newFailedHtml = @"<td style=""color: #dc2626; font-weight: bold;"">${(() => {
                    if (typeof testResults === 'undefined' || !testResults[s.code]) return 0;
                    const tr = testResults[s.code];
                    return Object.values(tr).filter(r => r.status === 'Failed').length;
                })()}</td>
                            <td style=""color: #d97706; font-weight: bold;"">${(() => {
                    if (typeof testResults === 'undefined' || !testResults[s.code]) return tcCount;
                    const tr = testResults[s.code];
                    const tested = Object.values(tr).filter(r => r.status === 'Passed' || r.status === 'Failed').length;
                    return tcCount - tested;
                })()}</td>";
        html = html.Replace(oldFailedPattern, newFailedHtml);

        return html;
    }
}
