namespace AzureSqlMcp.Application;

public interface ITableSchemaRepository
{
    Task<TableDdlData?> GetTableDdlAsync(string tableName);
    Task<string> GetRowCountAsync(string objectName);
}
