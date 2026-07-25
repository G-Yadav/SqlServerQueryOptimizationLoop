using AzureSqlMcp.Application;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AzureSqlMcp.Infrastructure;

public class SqlConnectionFactory(IOptions<SqlConnectionOptions> options) : ISqlConnectionFactory
{
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(options.Value.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
