namespace AzureSqlMcp.Application;

public interface ISpInspectionRepository
{
    Task<string?> GetDefinitionAsync(string spName, CancellationToken ct = default);
    Task<SpExecutionStats?> GetExecutionStatsAsync(string spName, CancellationToken ct = default);
}
