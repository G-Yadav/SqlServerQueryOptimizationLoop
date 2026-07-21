using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace AzureSqlMcp;

[McpServerToolType]
public class SpExecutionTools(ISpExecutionRepository repo)
{
    private static bool IsValidSpName(string name) =>
        Regex.IsMatch(name, @"^[\w\.\[\]]+$");

    [McpServerTool, Description("Executes a stored procedure by name and returns STATISTICS IO and STATISTICS TIME output for performance analysis.")]
    public async Task<string> RunBenchmark(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetManagerEmployees")] string spName,
        [Description("Optional comma-separated parameters in the form @param=value, e.g. @BusinessEntityID=1,@MaxDepth=3")] string? parameters = null)
    {
        if (!IsValidSpName(spName)) return "Invalid stored procedure name.";
        try { return await repo.RunBenchmarkAsync(spName, parameters); }
        catch (ArgumentException ex) { return $"Invalid parameters: {ex.Message}"; }
        catch (Exception ex)         { return $"Benchmark failed: {ex.Message}"; }
    }

    [McpServerTool, Description("Executes a stored procedure and returns the result set as CSV (no header, comma-separated, trimmed) for correctness verification against golden output.")]
    public async Task<string> ExecuteSp(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetReport")] string spName,
        [Description("Optional comma-separated parameters in the form @param=value, e.g. @StartDate=2024-01-01,@MaxRows=100")] string? parameters = null)
    {
        if (!IsValidSpName(spName)) return "Invalid stored procedure name.";
        try { return await repo.ExecuteSpAsync(spName, parameters); }
        catch (ArgumentException ex) { return $"Invalid parameters: {ex.Message}"; }
        catch (Exception ex)         { return $"Execution failed: {ex.Message}"; }
    }
}
