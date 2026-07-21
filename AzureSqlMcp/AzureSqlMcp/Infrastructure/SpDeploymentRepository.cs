using Microsoft.Data.SqlClient;

namespace AzureSqlMcp;

public class SpDeploymentRepository(ISqlConnectionFactory db) : ISpDeploymentRepository
{
    private static readonly string[] ValidPrefixes =
    [
        "CREATE OR ALTER PROCEDURE",
        "CREATE OR ALTER PROC",
        "ALTER PROCEDURE",
        "ALTER PROC"
    ];

    public async Task DeployAsync(string sql)
    {
        var trimmed = sql.TrimStart();
        if (!ValidPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Only ALTER PROCEDURE or CREATE OR ALTER PROCEDURE statements are allowed.");

        await using var conn = await db.OpenConnectionAsync();
        await new SqlCommand(sql, conn).ExecuteNonQueryAsync();
    }
}
