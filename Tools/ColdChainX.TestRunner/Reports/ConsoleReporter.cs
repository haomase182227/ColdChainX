using ColdChainX.TestRunner.Models;

namespace ColdChainX.TestRunner.Reports;

/// <summary>
/// Prints test results as a formatted table to console.
/// </summary>
public static class ConsoleReporter
{
    public static void Print(List<TestResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════╦═════════════╦═════════╦═══════╦════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║ Function ║ Test Case   ║ Status  ║  ms   ║ Message                                                    ║");
        Console.WriteLine("╠══════════╬═════════════╬═════════╬═══════╬════════════════════════════════════════════════════════════╣");

        foreach (var r in results)
        {
            var statusIcon = r.Status switch
            {
                TestStatus.Passed => "✓ PASS",
                TestStatus.Failed => "✗ FAIL",
                TestStatus.Skipped => "⊘ SKIP",
                _ => "? ????"
            };

            var statusColor = r.Status switch
            {
                TestStatus.Passed => ConsoleColor.Green,
                TestStatus.Failed => ConsoleColor.Red,
                TestStatus.Skipped => ConsoleColor.Yellow,
                _ => ConsoleColor.Gray
            };

            Console.Write("║ ");
            Console.Write(r.FunctionCode.PadRight(8));
            Console.Write(" ║ ");
            Console.Write($"{r.TestCaseId} ({r.TestCaseType})".PadRight(11));
            Console.Write(" ║ ");

            Console.ForegroundColor = statusColor;
            Console.Write(statusIcon.PadRight(7));
            Console.ResetColor();

            Console.Write(" ║ ");
            Console.Write(r.ElapsedMs.ToString().PadLeft(4) + "ms");
            Console.Write(" ║ ");
            Console.Write(Trunc(r.Message, 58).PadRight(58));
            Console.WriteLine(" ║");
        }

        Console.WriteLine("╠══════════╬═════════════╩═════════╩═══════╩════════════════════════════════════════════════════════════╣");

        // Summary
        var passed = results.Count(r => r.Status == TestStatus.Passed);
        var failed = results.Count(r => r.Status == TestStatus.Failed);
        var skipped = results.Count(r => r.Status == TestStatus.Skipped);
        var total = results.Count;

        var summary = $"Total: {total} | Passed: {passed} | Failed: {failed} | Skipped: {skipped}";
        var rate = total > 0 ? (passed * 100.0 / total).ToString("F1") : "0";

        Console.Write("║ SUMMARY  ║ ");
        Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.Write(summary.PadRight(82));
        Console.ResetColor();
        Console.WriteLine(" ║");

        Console.Write("║          ║ ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"Pass Rate: {rate}%".PadRight(82));
        Console.ResetColor();
        Console.WriteLine(" ║");

        Console.WriteLine("╚══════════╩═══════════════════════════════════════════════════════════════════════════════════════════╝");

        // Print failed tests details
        if (failed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n── {failed} FAILED TEST(S) ──");
            Console.ResetColor();

            foreach (var f in results.Where(r => r.Status == TestStatus.Failed))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"  ✗ {f.FunctionCode}.{f.TestCaseId}");
                Console.ResetColor();
                Console.Write($" [{f.TestCaseDesc}]");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    HTTP {f.HttpStatusCode}: {Trunc(f.Message, 120)}");
                if (f.ResponseBody != null)
                    Console.WriteLine($"    Body: {Trunc(f.ResponseBody, 200)}");
                Console.ResetColor();
            }
        }
    }

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}
