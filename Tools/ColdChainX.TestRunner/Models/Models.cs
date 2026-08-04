namespace ColdChainX.TestRunner.Models;

public class TestSpec
{
    public int No { get; set; }
    public string Code { get; set; } = "";
    public string FuncName { get; set; } = "";
    public string ClsName { get; set; } = "";
    public string Requirement { get; set; } = "";
    public string Description { get; set; } = "";
    public string SheetName { get; set; } = "";
    public List<string> Preconditions { get; set; } = new();
    public Dictionary<string, List<string>> Inputs { get; set; } = new();
    public List<string> Returns { get; set; } = new();
    public List<string> Exceptions { get; set; } = new();
    public List<string> Logs { get; set; } = new();
    public List<TestCaseSpec> TestCases { get; set; } = new();
}

public class TestCaseSpec
{
    public string Id { get; set; } = "";          // "UTCID01"
    public string Type { get; set; } = "";        // "N", "A", "B"
    public string Desc { get; set; } = "";
    public List<int> Pre { get; set; } = new();   // indices into Preconditions
    public Dictionary<string, int> Inp { get; set; } = new(); // input key → index
    public List<int> Ret { get; set; } = new();   // indices into Returns
    public int Exc { get; set; }                  // index into Exceptions
    public List<int> Log { get; set; } = new();   // indices into Logs
}

public class EndpointInfo
{
    public string Method { get; set; } = "GET";           // GET, POST, PUT, DELETE
    public string Url { get; set; } = "";                 // /api/auth/login
    public string AuthRole { get; set; } = "Anonymous";   // Anonymous, Admin, Customer, etc.
    public string BodyType { get; set; } = "None";        // None, Json, Form
    public string? Notes { get; set; }

    public EndpointInfo() { }
    public EndpointInfo(string method, string url, string authRole = "Anonymous", string bodyType = "None")
    {
        Method = method;
        Url = url;
        AuthRole = authRole;
        BodyType = bodyType;
    }
}

public class TestResult
{
    public string FunctionCode { get; set; } = "";
    public string TestCaseId { get; set; } = "";
    public string TestCaseType { get; set; } = "";
    public string TestCaseDesc { get; set; } = "";
    public TestStatus Status { get; set; }
    public string Message { get; set; } = "";
    public int HttpStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public long ElapsedMs { get; set; }

    // ── Matched indices for HTML export (which Return/Exception/Log rows were hit) ──
    public List<int> MatchedReturnIndices { get; set; } = new();
    public int MatchedExceptionIndex { get; set; } = -1;
    public List<int> MatchedLogIndices { get; set; } = new();

    public static TestResult Passed(string funcCode, string tcId, string tcType, string desc, string msg, int status = 200, long ms = 0)
        => new() { FunctionCode = funcCode, TestCaseId = tcId, TestCaseType = tcType, TestCaseDesc = desc, Status = TestStatus.Passed, Message = msg, HttpStatusCode = status, ElapsedMs = ms };

    public static TestResult Failed(string funcCode, string tcId, string tcType, string desc, string msg, int status = 0, string? body = null, long ms = 0)
        => new() { FunctionCode = funcCode, TestCaseId = tcId, TestCaseType = tcType, TestCaseDesc = desc, Status = TestStatus.Failed, Message = msg, HttpStatusCode = status, ResponseBody = body, ElapsedMs = ms };

    public static TestResult Skipped(string funcCode, string tcId, string tcType, string desc, string msg)
        => new() { FunctionCode = funcCode, TestCaseId = tcId, TestCaseType = tcType, TestCaseDesc = desc, Status = TestStatus.Skipped, Message = msg };
}

public enum TestStatus
{
    Passed,
    Failed,
    Skipped
}
