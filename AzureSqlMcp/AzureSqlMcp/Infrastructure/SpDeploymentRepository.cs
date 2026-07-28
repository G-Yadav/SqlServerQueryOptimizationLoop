using AzureSqlMcp.Application;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace AzureSqlMcp.Infrastructure;

public class SpDeploymentRepository(ISqlConnectionFactory db) : ISpDeploymentRepository
{
    private static readonly string[] ValidPrefixes =
    [
        "ALTER PROCEDURE",
        "ALTER PROC"
    ];

    private static readonly (string Label, Regex Pattern)[] InvalidPatterns =
    [
        ("xp_cmdshell",    new Regex(@"\bxp_cmdshell\b",    RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("OPENROWSET",     new Regex(@"\bOPENROWSET\b",     RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("OPENDATASOURCE", new Regex(@"\bOPENDATASOURCE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("OPENQUERY",      new Regex(@"\bOPENQUERY\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("BULK INSERT",    new Regex(@"\bBULK\s+INSERT\b",  RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("sp_OA*",         new Regex(@"\bsp_oa\w+\b",       RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("DROP",           new Regex(@"\bDROP\b",            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    ];

    public async Task DeployAsync(string sql, CancellationToken ct = default)
    {
        var trimmed = sql.TrimStart();
        if (!ValidPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Only ALTER PROCEDURE statements are allowed.");

        foreach (var (label, pattern) in InvalidPatterns)
            if (pattern.IsMatch(sql))
                throw new ArgumentException($"Stored procedure contains disallowed pattern: {label}.");

        await using var conn = await db.OpenConnectionAsync(ct);
        await new SqlCommand(sql, conn).ExecuteNonQueryAsync(ct);
    }
}
