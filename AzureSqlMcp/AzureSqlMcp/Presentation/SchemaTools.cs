using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureSqlMcp;

[McpServerToolType]
public class SchemaTools(ITableSchemaRepository repo)
{
    [McpServerTool, Description("Retrieves the DDL for a table including columns, data types, nullability, identity, defaults, primary key, unique constraints, indexes, and foreign keys.")]
    public async Task<string> GetTableDdl(
        [Description("The table name, optionally schema-qualified (e.g. dbo.Orders or Orders)")] string tableName)
    {
        var data = await repo.GetTableDdlAsync(tableName);
        return data == null ? $"Table '{tableName}' not found." : DdlFormatter.Format(data);
    }

    [McpServerTool, Description("Returns the exact row count for a table or view.")]
    public async Task<string> GetRowCount(
        [Description("The table or view name, optionally schema-qualified (e.g. dbo.Orders or dbo.vw_ActiveCustomers)")] string objectName)
    {
        try { return await repo.GetRowCountAsync(objectName); }
        catch (Exception ex) { return $"Row count failed: {ex.Message}"; }
    }
}
