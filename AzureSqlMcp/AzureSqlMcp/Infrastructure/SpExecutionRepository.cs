using AzureSqlMcp.Application;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AzureSqlMcp.Infrastructure;

public class SpExecutionRepository(ISqlConnectionFactory db) : ISpExecutionRepository
{
    public async Task<string> RunBenchmarkAsync(string spName, string? parameters)
    {
        await using var conn = await db.OpenConnectionAsync();
        var cmd = new SqlCommand(spName, conn) { CommandType = CommandType.StoredProcedure };
        AddParameters(cmd, parameters);

        var messages = new StringBuilder();
        conn.InfoMessage += (_, e) => messages.AppendLine(e.Message);

        await new SqlCommand("SET STATISTICS IO ON; SET STATISTICS TIME ON;", conn).ExecuteNonQueryAsync();
        await cmd.ExecuteNonQueryAsync();
        await new SqlCommand("SET STATISTICS IO OFF; SET STATISTICS TIME OFF;", conn).ExecuteNonQueryAsync();

        return messages.Length > 0 ? messages.ToString() : "No statistics returned.";
    }

    public async Task<string> ExecuteSpAsync(string spName, string? parameters)
    {
        await using var conn = await db.OpenConnectionAsync();
        var cmd = new SqlCommand(spName, conn) { CommandType = CommandType.StoredProcedure };
        AddParameters(cmd, parameters);

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

    public async Task<string> GetExecutionPlanAsync(string spName, string? parameters)
    {
        await using var conn = await db.OpenConnectionAsync();
        await new SqlCommand("SET STATISTICS XML ON", conn).ExecuteNonQueryAsync();

        var sb = new StringBuilder();
        {
            var cmd = new SqlCommand(spName, conn) { CommandType = CommandType.StoredProcedure };
            AddParameters(cmd, parameters);
            await using var reader = await cmd.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount != 1) continue;
                    var value = reader.IsDBNull(0) ? null : reader.GetString(0);
                    if (value?.TrimStart().StartsWith("<ShowPlanXML") == true)
                        sb.AppendLine(value);
                }
            } while (await reader.NextResultAsync());
        }

        await new SqlCommand("SET STATISTICS XML OFF", conn).ExecuteNonQueryAsync();
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "No execution plan returned.";
    }

    private static void AddParameters(SqlCommand cmd, string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters)) return;
        foreach (var param in parameters.Split(','))
        {
            var parts = param.Trim().Split('=', 2);
            if (parts.Length != 2)
                throw new ArgumentException($"Invalid parameter format: '{param}'. Expected @name=value.");
            var paramName  = parts[0].Trim();
            var paramValue = parts[1].Trim();
            if (!Regex.IsMatch(paramName, @"^@\w+$"))
                throw new ArgumentException($"Invalid parameter name: '{paramName}'. Must start with @ and contain only letters, digits, or underscores.");
            cmd.Parameters.AddWithValue(paramName, paramValue);
        }
    }
}
