namespace ColdChainX.TestRunner.Core;

/// <summary>
/// Variable store giống Postman Environment.
/// Lưu giá trị từ response trước → tự inject vào request sau.
/// </summary>
public class TestContext
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _verbose;

    public TestContext(bool verbose = true)
    {
        _verbose = verbose;
    }

    /// <summary>Lưu 1 giá trị vào context</summary>
    public void Set(string key, string value)
    {
        _variables[key] = value;
        if (_verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"   💾 ctx.{key} = {Truncate(value, 60)}");
            Console.ResetColor();
        }
    }

    /// <summary>Lấy giá trị từ context</summary>
    public string? Get(string key)
        => _variables.TryGetValue(key, out var v) ? v : null;

    /// <summary>Check xem key đã có chưa</summary>
    public bool Has(string key)
        => _variables.ContainsKey(key);

    /// <summary>Thay thế tất cả {{variable}} trong template string</summary>
    public string Resolve(string template)
    {
        var result = template;
        foreach (var (key, value) in _variables)
            result = result.Replace($"{{{{{key}}}}}", value);
        return result;
    }

    /// <summary>Lấy token theo role name</summary>
    public string? GetToken(string role)
    {
        // Thử tìm chính xác theo role
        if (_variables.TryGetValue($"{role}Token", out var token))
            return token;
        // Thử lowercase
        if (_variables.TryGetValue($"{role.ToLower()}Token", out token))
            return token;
        // Fallback: adminToken
        if (_variables.TryGetValue("adminToken", out token))
            return token;
        return null;
    }

    /// <summary>In toàn bộ context variables</summary>
    public void Dump()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n── Context Variables ──");
        foreach (var (key, value) in _variables.OrderBy(kv => kv.Key))
            Console.WriteLine($"  {key} = {Truncate(value, 80)}");
        Console.WriteLine("──────────────────────\n");
        Console.ResetColor();
    }

    public IReadOnlyDictionary<string, string> All => _variables;

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";
}
