using AzureSqlMcp.Application;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureSqlMcp.Presentation;

[McpServerToolType]
public class SpExecutionTools(ISpExecutionRepository repo)
{
    [McpServerTool, Description("Benchmarks a stored procedure: runs it nRuns+1 times (first run discarded as warm-up), then returns averaged STATISTICS IO/TIME as a single line: logical_reads=N, cpu_ms=N, elapsed_ms=N.")]
    public Task<string> RunBenchmark(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetManagerEmployees")] string spName,
        [Description("Optional semicolon-separated parameters in the form @param=value, e.g. @BusinessEntityID=1;@MaxDepth=3")] string? parameters = null,
        [Description("Number of measured runs after the warm-up. Defaults to 1.")] int nRuns = 1,
        CancellationToken ct = default)
        => ToolHelper.SafeExecute(spName, "Benchmark failed", () => repo.RunBenchmarkAsync(spName, parameters, nRuns, ct));

    [McpServerTool, Description("Benchmarks a stored procedure across several parameter sets in one call. Each set runs nRuns+1 times (first discarded as warm-up); returns one line per set (run_1, run_2, ...) as logical_reads=N, cpu_ms=N, elapsed_ms=N, followed by total_logical_reads=N. Use this for a full benchmark round over all test cases.")]
    public Task<string> BenchmarkAll(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetManagerEmployees")] string spName,
        [Description("Newline-separated parameter sets — one line per test case. Each line is a semicolon-separated @param=value set (e.g. @BusinessEntityID=2). A blank line means that set takes no parameters. Order is preserved: line 1 => run_1, line 2 => run_2, ...")] string parameterSets,
        [Description("Number of measured runs after the warm-up, applied to every set. Defaults to 1.")] int nRuns = 1,
        CancellationToken ct = default)
        => ToolHelper.SafeExecute(spName, "Benchmark failed", () => repo.RunBenchmarkBatchAsync(spName, SplitParameterSets(parameterSets), nRuns, ct));

    [McpServerTool, Description("Executes a stored procedure and returns the actual XML execution plan with runtime statistics. Useful for analysing query optimizer decisions, actual row counts, and index usage.")]
    public Task<string> GetExecutionPlan(
        [Description("The name of the stored procedure to inspect, e.g. dbo.uspGetReport")] string spName,
        [Description("Optional semicolon-separated parameters in the form @param=value, e.g. @StartDate=2024-01-01;@MaxRows=100")] string? parameters = null,
        CancellationToken ct = default)
        => ToolHelper.SafeExecute(spName, "Execution plan failed", () => repo.GetExecutionPlanAsync(spName, parameters, ct));

    [McpServerTool, Description("Executes a stored procedure and returns the result set as CSV (no header, comma-separated, trimmed). When outputFilePath is provided the CSV is written directly to that file and a row count is returned instead — use this for golden output capture and correctness checks.")]
    public Task<string> ExecuteSp(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetReport")] string spName,
        [Description("Optional semicolon-separated parameters in the form @param=value, e.g. @StartDate=2024-01-01;@MaxRows=100")] string? parameters = null,
        [Description("Optional absolute file path. When supplied the CSV rows are written to this file and a row count confirmation is returned instead of the CSV content.")] string? outputFilePath = null,
        CancellationToken ct = default)
        => ToolHelper.SafeExecute(spName, "Execution failed", () => repo.ExecuteSpAsync(spName, parameters, outputFilePath, ct));

    // Splits the newline-separated parameterSets argument into one entry per test case,
    // mapping blank lines to null (no parameters). Empty input yields a single no-parameter set.
    private static IReadOnlyList<string?> SplitParameterSets(string? parameterSets)
    {
        var trimmed = parameterSets?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return new string?[] { null };

        return trimmed.Replace("\r", "").Split('\n')
            .Select(line => string.IsNullOrWhiteSpace(line) ? null : line.Trim())
            .ToArray();
    }
}
