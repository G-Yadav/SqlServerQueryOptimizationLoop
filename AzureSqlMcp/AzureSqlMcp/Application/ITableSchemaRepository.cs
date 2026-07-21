namespace AzureSqlMcp;

public interface ITableSchemaRepository
{
    Task<TableDdlData?> GetTableDdlAsync(string tableName);
}
