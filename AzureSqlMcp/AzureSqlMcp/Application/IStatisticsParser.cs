namespace AzureSqlMcp.Application;

/// <summary>
/// Parses raw SQL Server STATISTICS IO/TIME text into the aggregate numbers the
/// optimization loop cares about, so the raw text never has to reach the model.
/// </summary>
public interface IStatisticsParser
{
    BenchmarkStats Parse(string statisticsText);
}

/// <summary>Aggregate result of one benchmarked run: total logical reads and timing.</summary>
public readonly record struct BenchmarkStats(long LogicalReads, long CpuMs, long ElapsedMs);
