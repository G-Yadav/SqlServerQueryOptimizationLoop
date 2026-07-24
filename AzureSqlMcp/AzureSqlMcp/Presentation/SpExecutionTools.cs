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
        [Description("Optional comma-separated parameters in the form @param=value, e.g. @BusinessEntityID=1,@MaxDepth=3")] string? parameters = null)
        => SpToolHelper.SafeExecute(spName, "Benchmark failed", () => repo.RunBenchmarkAsync(spName, parameters));

    [McpServerTool, Description("Executes a stored procedure and returns the actual XML execution plan with runtime statistics. Useful for analysing query optimizer decisions, actual row counts, and index usage.")]
    public Task<string> GetExecutionPlan(
        [Description("The name of the stored procedure to inspect, e.g. dbo.uspGetReport")] string spName,
        [Description("Optional comma-separated parameters in the form @param=value, e.g. @StartDate=2024-01-01,@MaxRows=100")] string? parameters = null)
        => SpToolHelper.SafeExecute(spName, "Execution plan failed", () => repo.GetExecutionPlanAsync(spName, parameters));

    [McpServerTool, Description("Executes a stored procedure and returns the result set as CSV (no header, comma-separated, trimmed) for correctness verification against golden output.")]
    public Task<string> ExecuteSp(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetReport")] string spName,
        [Description("Optional comma-separated parameters in the form @param=value, e.g. @StartDate=2024-01-01,@MaxRows=100")] string? parameters = null)
        => SpToolHelper.SafeExecute(spName, "Execution failed", () => repo.ExecuteSpAsync(spName, parameters));
}
