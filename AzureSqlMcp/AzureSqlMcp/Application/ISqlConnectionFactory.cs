using Microsoft.Data.SqlClient;

namespace AzureSqlMcp;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenConnectionAsync();
}
