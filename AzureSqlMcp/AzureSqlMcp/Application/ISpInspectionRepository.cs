namespace AzureSqlMcp;

public interface ISpInspectionRepository
{
    Task<string?> GetDefinitionAsync(string spName);
    Task<SpExecutionStats?> GetExecutionStatsAsync(string spName);
}
