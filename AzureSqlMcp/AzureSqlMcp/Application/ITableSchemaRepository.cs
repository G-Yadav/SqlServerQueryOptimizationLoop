namespace AzureSqlMcp.Application;

public interface ITableSchemaRepository
{
    Task<TableDdlData?> GetTableDdlAsync(string tableName, CancellationToken ct = default);
    Task<string> GetRowCountAsync(string objectName, CancellationToken ct = default);
}
