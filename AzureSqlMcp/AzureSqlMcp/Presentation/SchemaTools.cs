using AzureSqlMcp.Application;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureSqlMcp.Presentation;

[McpServerToolType]
public class SchemaTools(ITableSchemaRepository repo)
{
    [McpServerTool, Description("Retrieves the DDL for a table including columns, data types, nullability, identity, defaults, primary key, unique constraints, indexes, and foreign keys.")]
    public Task<string> GetTableDdl(
        [Description("The table name, optionally schema-qualified (e.g. dbo.Orders or Orders)")] string tableName,
        CancellationToken ct = default)
        => ToolHelper.SafeExecute(tableName, "DDL retrieval failed", async () =>
        {
            var data = await repo.GetTableDdlAsync(tableName, ct);
            return data == null ? $"Table '{tableName}' not found." : DdlFormatter.Format(data);
        });

    [McpServerTool, Description("Returns the exact row count for a table or view.")]
    public Task<string> GetRowCount(
        [Description("The table or view name, optionally schema-qualified (e.g. dbo.Orders or dbo.vw_ActiveCustomers)")] string objectName,
        CancellationToken ct = default)
        => ToolHelper.SafeExecute(objectName, "Row count failed", () => repo.GetRowCountAsync(objectName, ct));
}
