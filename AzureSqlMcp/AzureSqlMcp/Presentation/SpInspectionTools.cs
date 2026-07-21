using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureSqlMcp;

[McpServerToolType]
public class SpInspectionTools(ISpInspectionRepository repo)
{
    [McpServerTool, Description("Retrieves the full T-SQL definition of a stored procedure from the database.")]
    public async Task<string> GetSpDefinition(
        [Description("The name of the stored procedure to retrieve the definition for.")] string spName)
    {
        var definition = await repo.GetDefinitionAsync(spName);
        return definition ?? "Not found";
    }

    [McpServerTool, Description("Gets execution statistics for a stored procedure from DMVs, including avg CPU, elapsed time, and logical reads.")]
    public async Task<string> GetExecutionStats(
        [Description("The name of the stored procedure to retrieve execution stats for.")] string spName)
    {
        var stats = await repo.GetExecutionStatsAsync(spName);
        if (stats == null) return "No stats found — SP may not have been executed yet.";
        return $"execution_count: {stats.ExecutionCount}\n" +
               $"avg_elapsed_us: {stats.AvgElapsedUs}\n" +
               $"avg_logical_reads: {stats.AvgLogicalReads}\n" +
               $"avg_cpu_us: {stats.AvgCpuUs}\n" +
               $"last_execution_time: {stats.LastExecutionTime}";
    }
}
