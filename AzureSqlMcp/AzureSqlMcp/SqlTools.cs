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

    [McpServerTool, Description("Retrieves the DDL for a table including columns, data types, nullability, identity, defaults, primary key, unique constraints, indexes, and foreign keys.")]
    public static async Task<string> GetTableDdl(
        [Description("The table name, optionally schema-qualified (e.g. dbo.Orders or Orders)")] string tableName)
    {
        await using var conn = new SqlConnection(ConnString);
        await conn.OpenAsync();

        var idCmd = new SqlCommand("SELECT OBJECT_ID(@name, 'U')", conn);
        idCmd.Parameters.AddWithValue("@name", tableName);
        var idResult = await idCmd.ExecuteScalarAsync();
        if (idResult == null || idResult == DBNull.Value) return $"Table '{tableName}' not found.";
        var objectId = (int)idResult;

        string schemaName, tblName;
        {
            var nameCmd = new SqlCommand("SELECT SCHEMA_NAME(schema_id), name FROM sys.tables WHERE object_id = @id", conn);
            nameCmd.Parameters.AddWithValue("@id", objectId);
            await using var r = await nameCmd.ExecuteReaderAsync();
            await r.ReadAsync();
            schemaName = r.GetString(0);
            tblName = r.GetString(1);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{schemaName}].[{tblName}]");
        sb.AppendLine("(");

        var colCmd = new SqlCommand(@"
            SELECT c.name, tp.name, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity,
                   ic2.seed_value, ic2.increment_value,
                   dc.definition
            FROM sys.columns c
            JOIN sys.types tp ON c.user_type_id = tp.user_type_id
            LEFT JOIN sys.identity_columns ic2 ON c.object_id = ic2.object_id AND c.column_id = ic2.column_id
            LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
            WHERE c.object_id = @id
            ORDER BY c.column_id", conn);
        colCmd.Parameters.AddWithValue("@id", objectId);

        var colDefs = new List<string>();
        await using (var r = await colCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var col = r.GetString(0);
                var typeName = r.GetString(1);
                var maxLen = r.GetInt16(2);
                var prec = r.GetByte(3);
                var scale = r.GetByte(4);
                var nullable = r.GetBoolean(5);
                var isIdentity = r.GetBoolean(6);
                var defaultDef = r.IsDBNull(9) ? null : r.GetString(9);

                var typeStr = typeName.ToLower() switch
                {
                    "nvarchar" or "nchar" => maxLen == -1 ? $"{typeName}(MAX)" : $"{typeName}({maxLen / 2})",
                    "varchar" or "char" or "varbinary" or "binary" => maxLen == -1 ? $"{typeName}(MAX)" : $"{typeName}({maxLen})",
                    "decimal" or "numeric" => $"{typeName}({prec},{scale})",
                    "datetime2" or "time" or "datetimeoffset" => $"{typeName}({scale})",
                    "float" => prec <= 24 ? "real" : "float",
                    _ => typeName
                };

                var line = $"    [{col}] {typeStr}";
                if (isIdentity)
                {
                    var seed = r.IsDBNull(7) ? 1L : Convert.ToInt64(r.GetValue(7));
                    var incr = r.IsDBNull(8) ? 1L : Convert.ToInt64(r.GetValue(8));
                    line += $" IDENTITY({seed},{incr})";
                }
                line += nullable ? " NULL" : " NOT NULL";
                if (defaultDef != null) line += $" DEFAULT {defaultDef}";
                colDefs.Add(line);
            }
        }

        var pkCmd = new SqlCommand(@"
            SELECT k.name,
                   STRING_AGG('[' + c.name + ']', ', ') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.key_constraints k
            JOIN sys.index_columns ic ON k.parent_object_id = ic.object_id AND k.unique_index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE k.parent_object_id = @id AND k.type = 'PK'
            GROUP BY k.name", conn);
        pkCmd.Parameters.AddWithValue("@id", objectId);
        await using (var r = await pkCmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
                colDefs.Add($"    CONSTRAINT [{r.GetString(0)}] PRIMARY KEY ({r.GetString(1)})");
        }

        var ucCmd = new SqlCommand(@"
            SELECT k.name,
                   STRING_AGG('[' + c.name + ']', ', ') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.key_constraints k
            JOIN sys.index_columns ic ON k.parent_object_id = ic.object_id AND k.unique_index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE k.parent_object_id = @id AND k.type = 'UQ'
            GROUP BY k.name", conn);
        ucCmd.Parameters.AddWithValue("@id", objectId);
        await using (var r = await ucCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                colDefs.Add($"    CONSTRAINT [{r.GetString(0)}] UNIQUE ({r.GetString(1)})");
        }

        sb.AppendLine(string.Join(",\n", colDefs));
        sb.AppendLine(");");

        var idxCmd = new SqlCommand(@"
            SELECT i.name, i.type_desc, i.is_unique,
                   STRING_AGG('[' + c.name + ']' + CASE ic.is_descending_key WHEN 1 THEN ' DESC' ELSE '' END, ', ')
                       WITHIN GROUP (ORDER BY ic.key_ordinal),
                   (SELECT STRING_AGG('[' + c2.name + ']', ', ') WITHIN GROUP (ORDER BY ic2.index_column_id)
                    FROM sys.index_columns ic2
                    JOIN sys.columns c2 ON ic2.object_id = c2.object_id AND ic2.column_id = c2.column_id
                    WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id AND ic2.is_included_column = 1)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.object_id = @id AND i.type > 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
            GROUP BY i.name, i.type_desc, i.is_unique, i.index_id", conn);
        idxCmd.Parameters.AddWithValue("@id", objectId);
        await using (var r = await idxCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var clust = r.GetString(1) == "CLUSTERED" ? "CLUSTERED " : "NONCLUSTERED ";
                var unique = r.GetBoolean(2) ? "UNIQUE " : "";
                var inclCols = r.IsDBNull(4) ? null : r.GetString(4);
                sb.Append($"\nCREATE {unique}{clust}INDEX [{r.GetString(0)}] ON [{schemaName}].[{tblName}] ({r.GetString(3)})");
                if (inclCols != null) sb.Append($" INCLUDE ({inclCols})");
                sb.AppendLine(";");
            }
        }

        var fkCmd = new SqlCommand(@"
            SELECT fk.name,
                   STRING_AGG('[' + fc.name + ']', ', ') WITHIN GROUP (ORDER BY fkc.constraint_column_id),
                   '[' + SCHEMA_NAME(rt.schema_id) + '].[' + rt.name + ']',
                   STRING_AGG('[' + rc.name + ']', ', ') WITHIN GROUP (ORDER BY fkc.constraint_column_id),
                   fk.delete_referential_action_desc,
                   fk.update_referential_action_desc
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.columns fc ON fkc.parent_object_id = fc.object_id AND fkc.parent_column_id = fc.column_id
            JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
            JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
            WHERE fk.parent_object_id = @id
            GROUP BY fk.name, rt.name, rt.schema_id, fk.delete_referential_action_desc, fk.update_referential_action_desc", conn);
        fkCmd.Parameters.AddWithValue("@id", objectId);
        await using (var r = await fkCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var del = r.GetString(4);
                var upd = r.GetString(5);
                sb.Append($"\n-- FK [{r.GetString(0)}]: ({r.GetString(1)}) REFERENCES {r.GetString(2)} ({r.GetString(3)})");
                if (del != "NO_ACTION") sb.Append($" ON DELETE {del.Replace("_", " ")}");
                if (upd != "NO_ACTION") sb.Append($" ON UPDATE {upd.Replace("_", " ")}");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
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
