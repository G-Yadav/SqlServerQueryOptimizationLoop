using Microsoft.Data.SqlClient;

namespace AzureSqlMcp.Application;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenConnectionAsync();
}
