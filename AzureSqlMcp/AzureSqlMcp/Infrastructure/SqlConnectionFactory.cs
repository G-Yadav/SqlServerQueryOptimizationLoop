using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AzureSqlMcp;

public class SqlConnectionFactory(IConfiguration config) : ISqlConnectionFactory
{
    private string ConnString =>
        config["AZURE_CONN_STRING"] ?? throw new InvalidOperationException("AZURE_CONN_STRING is not set.");

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var conn = new SqlConnection(ConnString);
        await conn.OpenAsync();
        return conn;
    }
}
