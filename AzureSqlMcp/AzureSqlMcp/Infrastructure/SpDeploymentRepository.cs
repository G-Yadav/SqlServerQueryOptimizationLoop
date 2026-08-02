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

    // Matches the start of a DELETE statement and captures its target table, skipping the
    // optional TOP (n) [PERCENT] clause and the optional FROM keyword. The target stops at the
    // first whitespace, comma, open-paren, or semicolon. Only DELETEs whose target is a temp
    // table (#... / ##...) or table variable (@...) are permitted; everything else is rejected.
    private static readonly Regex DeleteStatement = new(
        @"\bDELETE\b\s+(?:TOP\s*\(\s*\d+\s*\)\s*(?:PERCENT\s*)?)?(?:FROM\s+)?(?<target>[^\s,(;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task DeployAsync(string sql, CancellationToken ct = default)
    {
        var trimmed = sql.TrimStart();
        if (!ValidPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Only ALTER PROCEDURE statements are allowed.");

        foreach (var (label, pattern) in InvalidPatterns)
            if (pattern.IsMatch(sql))
                throw new ArgumentException($"Stored procedure contains disallowed pattern: {label}.");

        ValidateDeletesTargetTempObjectsOnly(sql);

        await using var conn = await db.OpenConnectionAsync(ct);
        await new SqlCommand(sql, conn).ExecuteNonQueryAsync(ct);
    }

    // A DELETE is only safe here if it targets session-local, non-persistent storage: a temp
    // table (#temp / ##global) or a table variable (@var). Deletes against a persistent table
    // are rejected. This is a conservative guard: the aliased multi-table form
    // (DELETE alias FROM #temp alias JOIN ...) is rejected because the token after DELETE is the
    // alias, not the temp table — rewrite it as a direct DELETE [FROM] #temp. Note it does not
    // cover the MERGE ... WHEN MATCHED THEN DELETE construct, which this pattern does not match.
    private static void ValidateDeletesTargetTempObjectsOnly(string sql)
    {
        foreach (Match match in DeleteStatement.Matches(sql))
        {
            var target = match.Groups["target"].Value.TrimStart('[');
            if (!target.StartsWith('#') && !target.StartsWith('@'))
                throw new ArgumentException(
                    $"DELETE is only allowed against a temp table (#...) or table variable (@...); " +
                    $"found target '{match.Groups["target"].Value}'.");
        }
    }
}
