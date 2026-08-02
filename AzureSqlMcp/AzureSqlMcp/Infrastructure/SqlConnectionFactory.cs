using AzureSqlMcp.Application;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AzureSqlMcp.Infrastructure;

public class SqlConnectionFactory(IOptions<SqlConnectionOptions> options, IAccessTokenProvider tokenProvider)
    : ISqlConnectionFactory
{
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(options.Value.ConnectionString);
        try
        {
            var token = await tokenProvider.GetAccessTokenAsync(ct);
            if (token is not null)
                conn.AccessToken = token;

            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }
}
