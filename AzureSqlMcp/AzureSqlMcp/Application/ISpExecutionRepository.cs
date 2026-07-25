namespace AzureSqlMcp.Application;

public interface ISpExecutionRepository
{
    Task<string> RunBenchmarkAsync(string spName, string? parameters, CancellationToken ct = default);
    Task<string> ExecuteSpAsync(string spName, string? parameters, CancellationToken ct = default);
    Task<string> GetExecutionPlanAsync(string spName, string? parameters, CancellationToken ct = default);
}
