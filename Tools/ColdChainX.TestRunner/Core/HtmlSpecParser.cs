using System.Text.Json;
using System.Text.RegularExpressions;
using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Parse embedded specsData JSON from HTML spec file.
/// Designed specifically for SP26SE002_Unit_Test_ColdChainX_*_Functions_Spec.html format.
/// </summary>
public static class HtmlSpecParser
{
    public static List<TestSpec> Parse(string htmlFilePath)
    {
        if (!File.Exists(htmlFilePath))
            throw new FileNotFoundException($"HTML spec file not found: {htmlFilePath}");

        var html = File.ReadAllText(htmlFilePath);

        // Extract the JSON array from: const specsData = [{...}, {...}, ...];
        var match = Regex.Match(html, @"const\s+specsData\s*=\s*(\[.*?\])\s*;", RegexOptions.Singleline);
        if (!match.Success)
            throw new InvalidDataException("Could not find 'const specsData = [...]' in HTML file.");

        var jsonText = match.Groups[1].Value;
        var jsonArray = JsonDocument.Parse(jsonText).RootElement;

        var specs = new List<TestSpec>();

        foreach (var item in jsonArray.EnumerateArray())
        {
            var spec = new TestSpec
            {
                No = item.TryGetProperty("no", out var noEl) ? noEl.GetInt32() : specs.Count + 1,
                Code = item.TryGetProperty("code", out var codeEl) ? codeEl.GetString() ?? "" : "",
                FuncName = item.TryGetProperty("func_name", out var fnEl) ? fnEl.GetString() ?? "" : "",
                ClsName = item.TryGetProperty("cls_name", out var clsEl) ? clsEl.GetString() ?? "" : "",
                Requirement = item.TryGetProperty("requirement", out var reqEl) ? reqEl.GetString() ?? "" : "",
                Description = item.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "",
                SheetName = item.TryGetProperty("sheet_name", out var snEl) ? snEl.GetString() ?? "" : "",
                Preconditions = item.TryGetProperty("preconditions", out var preEl) ? ParseStringArray(preEl) : new(),
                Returns = item.TryGetProperty("returns", out var retEl) ? ParseStringArray(retEl) : new(),
                Exceptions = item.TryGetProperty("exceptions", out var excEl) ? ParseStringArray(excEl) : new(),
                Logs = item.TryGetProperty("logs", out var logEl) ? ParseStringArray(logEl) : new(),
                Inputs = item.TryGetProperty("inputs", out var inpEl) ? ParseInputsDict(inpEl) : new(),
            };

            // Parse test cases
            foreach (var tc in item.GetProperty("test_cases").EnumerateArray())
            {
                var testCase = new TestCaseSpec
                {
                    Id = tc.GetProperty("id").GetString() ?? "",
                    Type = tc.GetProperty("type").GetString() ?? "",
                    Desc = tc.GetProperty("desc").GetString() ?? "",
                    Exc = tc.GetProperty("exc").GetInt32(),
                    Pre = ParseIntArray(tc.GetProperty("pre")),
                    Ret = ParseIntArray(tc.GetProperty("ret")),
                    Log = ParseIntArray(tc.GetProperty("log")),
                    Inp = ParseIntDict(tc.GetProperty("inp")),
                };
                spec.TestCases.Add(testCase);
            }

            specs.Add(spec);
        }

        return specs;
    }

    private static List<string> ParseStringArray(JsonElement el)
    {
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
            list.Add(item.GetString() ?? "");
        return list;
    }

    private static List<int> ParseIntArray(JsonElement el)
    {
        var list = new List<int>();
        foreach (var item in el.EnumerateArray())
            list.Add(item.GetInt32());
        return list;
    }

    private static Dictionary<string, int> ParseIntDict(JsonElement el)
    {
        var dict = new Dictionary<string, int>();
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = prop.Value.GetInt32();
        return dict;
    }

    private static Dictionary<string, List<string>> ParseInputsDict(JsonElement el)
    {
        var dict = new Dictionary<string, List<string>>();
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = ParseStringArray(prop.Value);
        return dict;
    }
}
