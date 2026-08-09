using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Reports;

public static class HtmlResultExporter
{
    public static string Export(string sourceHtmlPath, List<TestSpec> specs, List<TestResult> results, string? outputPath = null)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            var dir = Path.GetDirectoryName(sourceHtmlPath) ?? ".";
            var name = Path.GetFileNameWithoutExtension(sourceHtmlPath);
            outputPath = Path.Combine(dir, $"{name}_Result.html");
        }

        var html = File.ReadAllText(sourceHtmlPath, Encoding.UTF8);

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

        var injectionPoint = "const specsData =";
        var injectionIdx = html.IndexOf(injectionPoint, StringComparison.Ordinal);
        if (injectionIdx < 0)
            throw new InvalidDataException("Cannot find 'const specsData =' in HTML file for result injection.");

        var testResultsVar = $"const testResults = {testResultsJson};\n        ";
        html = html.Insert(injectionIdx, testResultsVar);

        html = InjectResultRendering(html);

        html = InjectStatsRendering(html);

        File.WriteAllText(outputPath, html, Encoding.UTF8);
        return outputPath;
    }

    private static string InjectResultRendering(string html)
    {

        var confirmStart = html.IndexOf("// Build Confirm Section", StringComparison.Ordinal);
        if (confirmStart < 0)
            confirmStart = html.IndexOf("// Confirm Section", StringComparison.Ordinal);
        if (confirmStart < 0)
            return html;

        var confirmEnd = html.IndexOf("// Construct full sheet HTML", confirmStart, StringComparison.Ordinal);
        if (confirmEnd < 0)
            confirmEnd = html.IndexOf("sheet.innerHTML", confirmStart, StringComparison.Ordinal);
        if (confirmEnd < 0)
            return html;

        var beforeConfirm = html[..confirmStart];
        var afterConfirm = html[confirmEnd..];

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
                        else if ((!tr.ret || tr.ret.length === 0) && tc.ret && tc.ret.includes(idx)) mark = 'O';
                    } else if (tc.ret && tc.ret.includes(idx)) {
                        mark = 'O';
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
                            else if (tr.exc < 0 && tc.exc === idx) mark = 'O';
                        } else if (tc.exc === idx) {
                            mark = 'O';
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
                            else if ((!tr.log || tr.log.length === 0) && tc.log && tc.log.includes(idx)) mark = 'O';
                        } else if (tc.log && tc.log.includes(idx)) {
                            mark = 'O';
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

    private static string InjectStatsRendering(string html)
    {

        var oldPassedPattern = @"<td class=""val-passed"">0</td>";
        var newPassedHtml = @"<td class=""val-passed"">${(() => {
                    if (typeof testResults === 'undefined' || !testResults[s.code]) return 0;
                    const tr = testResults[s.code];
                    return Object.values(tr).filter(r => r.status === 'Passed').length;
                })()}</td>";
        html = html.Replace(oldPassedPattern, newPassedHtml);

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
