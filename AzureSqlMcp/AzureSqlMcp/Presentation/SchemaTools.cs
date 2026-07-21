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
}
