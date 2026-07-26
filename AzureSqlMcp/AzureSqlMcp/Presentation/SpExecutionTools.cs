using AzureSqlMcp.Application;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureSqlMcp.Presentation;

[McpServerToolType]
public class SpExecutionTools(ISpExecutionRepository repo)
{
    [McpServerTool, Description("Executes a stored procedure by name and returns STATISTICS IO and STATISTICS TIME output for performance analysis.")]
    public Task<string> RunBenchmark(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetManagerEmployees")] string spName,
        [Description("Optional semicolon-separated parameters in the form @param=value, e.g. @BusinessEntityID=1;@MaxDepth=3")] string? parameters = null,
        CancellationToken ct = default)
        => ToolHelper.SafeExecute(spName, "Benchmark failed", () => repo.RunBenchmarkAsync(spName, parameters, ct));

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
}
