namespace AzureSqlMcp;

public interface ISpExecutionRepository
{
    Task<string> RunBenchmarkAsync(string spName, string? parameters);
    Task<string> ExecuteSpAsync(string spName, string? parameters);
}
