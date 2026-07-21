using Microsoft.Data.SqlClient;

namespace AzureSqlMcp;

public class SpInspectionRepository(ISqlConnectionFactory db) : ISpInspectionRepository
{
    public async Task<string?> GetDefinitionAsync(string spName)
    {
        await using var conn = await db.OpenConnectionAsync();
        var cmd = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(@name))", conn);
        cmd.Parameters.AddWithValue("@name", spName);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : result.ToString();
    }

    public async Task<SpExecutionStats?> GetExecutionStatsAsync(string spName)
    {
        await using var conn = await db.OpenConnectionAsync();
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
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new SpExecutionStats(
            ExecutionCount:   Convert.ToInt64(r["execution_count"]),
            AvgElapsedUs:     Convert.ToInt64(r["avg_elapsed_us"]),
            AvgLogicalReads:  Convert.ToInt64(r["avg_logical_reads"]),
            AvgCpuUs:         Convert.ToInt64(r["avg_cpu_us"]),
            LastExecutionTime: (DateTime)r["last_execution_time"]);
    }
}
