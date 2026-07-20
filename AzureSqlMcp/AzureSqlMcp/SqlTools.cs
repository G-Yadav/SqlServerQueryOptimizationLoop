using System.ComponentModel;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace AzureSqlMcp;

[McpServerToolType]
public static class SqlTools {
    private static string ConnString => Environment.GetEnvironmentVariable("AZURE_CONN_STRING")!;

    [McpServerTool, Description("Retrieves the full T-SQL definition of a stored procedure from the database.")]
    public static async Task<string> GetSpDefinition([Description("The name of the stored procedure to retrieve the definition for.")] string spName) {
        await using var conn = new SqlConnection(ConnString);
        await conn.OpenAsync();
        var cmd = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(@name))", conn);
        cmd.Parameters.AddWithValue("@name", spName);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? "Not found";
    }

    [McpServerTool, Description("Gets execution statistics for a stored procedure from DMVs, including avg CPU, elapsed time, and logical reads.")]
    public static async Task<string> GetExecutionStats([Description("The name of the stored procedure to retrieve execution stats for.")] string spName) {
        await using var conn = new SqlConnection(ConnString);
        await conn.OpenAsync();
        var cmd = new SqlCommand(@"
            SELECT
                execution_count,
                total_elapsed_time / execution_count AS avg_elapsed_us,
                total_logical_reads / execution_count AS avg_logical_reads,
                total_worker_time / execution_count AS avg_cpu_us,
                last_execution_time
            FROM sys.dm_exec_procedure_stats ps
            JOIN sys.objects o ON ps.object_id = o.object_id
            WHERE o.name = @name", conn);
        cmd.Parameters.AddWithValue("@name", spName);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return "No stats found — SP may not have been executed yet.";
        return $"execution_count: {reader["execution_count"]}\n" +
               $"avg_elapsed_us: {reader["avg_elapsed_us"]}\n" +
               $"avg_logical_reads: {reader["avg_logical_reads"]}\n" +
               $"avg_cpu_us: {reader["avg_cpu_us"]}\n" +
               $"last_execution_time: {reader["last_execution_time"]}";
    }

    [McpServerTool, Description("Deploys a stored procedure to the database. The SQL must start with ALTER PROCEDURE or CREATE OR ALTER PROCEDURE.")]
    public static async Task<string> DeploySp([Description("The full T-SQL ALTER PROCEDURE or CREATE OR ALTER PROCEDURE statement to execute.")] string sql) {
        var trimmed = sql.TrimStart();
        if (!trimmed.StartsWith("ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("ALTER PROC", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("CREATE OR ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("CREATE OR ALTER PROC", StringComparison.OrdinalIgnoreCase))
            return "Invalid SQL: only ALTER PROCEDURE or CREATE OR ALTER PROCEDURE statements are allowed.";

        await using var conn = new SqlConnection(ConnString);
        await conn.OpenAsync();
        var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
        return "Stored procedure deployed successfully.";
    }

    [McpServerTool, Description("Executes a stored procedure by name and returns STATISTICS IO and STATISTICS TIME output for performance analysis.")]
    public static async Task<string> RunBenchmark(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetManagerEmployees")] string spName,
        [Description("Optional comma-separated parameters in the form @param=value, e.g. @BusinessEntityID=1,@MaxDepth=3")] string? parameters = null)
    {
        if (!Regex.IsMatch(spName, @"^[\w\.\[\]]+$"))
            return "Invalid stored procedure name.";

        await using var conn = new SqlConnection(ConnString);
        await conn.OpenAsync();
        var cmd = new SqlCommand(spName, conn) { CommandType = CommandType.StoredProcedure };

        if (!string.IsNullOrWhiteSpace(parameters))
        {
            foreach (var param in parameters.Split(','))
            {
                var parts = param.Trim().Split('=', 2);
                if (parts.Length != 2) return $"Invalid parameter format: '{param}'. Expected @name=value.";
                var paramName = parts[0].Trim();
                var paramValue = parts[1].Trim();
                if (!Regex.IsMatch(paramName, @"^@\w+$"))
                    return $"Invalid parameter name: '{paramName}'. Must start with @ and contain only letters, digits, or underscores.";
                cmd.Parameters.AddWithValue(paramName, paramValue);
            }
        }

        var messages = new StringBuilder();
        conn.InfoMessage += (_, e) => messages.AppendLine(e.Message);

        await new SqlCommand("SET STATISTICS IO ON; SET STATISTICS TIME ON;", conn).ExecuteNonQueryAsync();
        await cmd.ExecuteNonQueryAsync();
        await new SqlCommand("SET STATISTICS IO OFF; SET STATISTICS TIME OFF;", conn).ExecuteNonQueryAsync();

        return messages.Length > 0 ? messages.ToString() : "No statistics returned.";
    }

    [McpServerTool, Description("Executes a stored procedure and returns the result set as CSV (no header, comma-separated, trimmed) for correctness verification against golden output.")]
    public static async Task<string> ExecuteSp(
        [Description("The name of the stored procedure to execute, e.g. dbo.uspGetReport")] string spName,
        [Description("Optional comma-separated parameters in the form @param=value, e.g. @StartDate=2024-01-01,@MaxRows=100")] string? parameters = null)
    {
        if (!Regex.IsMatch(spName, @"^[\w\.\[\]]+$"))
            return "Invalid stored procedure name.";

        await using var conn = new SqlConnection(ConnString);
        await conn.OpenAsync();
        var cmd = new SqlCommand(spName, conn) { CommandType = CommandType.StoredProcedure };

        if (!string.IsNullOrWhiteSpace(parameters))
        {
            foreach (var param in parameters.Split(','))
            {
                var parts = param.Trim().Split('=', 2);
                if (parts.Length != 2) return $"Invalid parameter format: '{param}'. Expected @name=value.";
                var paramName = parts[0].Trim();
                var paramValue = parts[1].Trim();
                if (!Regex.IsMatch(paramName, @"^@\w+$"))
                    return $"Invalid parameter name: '{paramName}'. Must start with @ and contain only letters, digits, or underscores.";
                cmd.Parameters.AddWithValue(paramName, paramValue);
            }
        }

        var sb = new StringBuilder();
        await using var reader = await cmd.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
            {
                var cols = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    cols[i] = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()!.Trim();
                sb.AppendLine(string.Join(",", cols));
            }
        } while (await reader.NextResultAsync());

        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(empty result set)";
    }
}
