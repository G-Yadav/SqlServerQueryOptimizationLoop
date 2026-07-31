namespace AzureSqlMcp.Application;

public interface ISpExecutionRepository
{
    Task<string> RunBenchmarkAsync(string spName, string? parameters, int nRuns = 1, CancellationToken ct = default);
    Task<string> RunBenchmarkBatchAsync(string spName, IReadOnlyList<string?> parameterSets, int nRuns = 1, CancellationToken ct = default);
    Task<string> ExecuteSpAsync(string spName, string? parameters, string? outputFilePath = null, CancellationToken ct = default);
    Task<string> GetExecutionPlanAsync(string spName, string? parameters, CancellationToken ct = default);
}
