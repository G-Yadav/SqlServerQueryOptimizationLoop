namespace AzureSqlMcp.Infrastructure;

public sealed class SqlConnectionOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional shell command that prints an Entra ID access token for the SQL resource
    /// (e.g. <c>az account get-access-token --resource https://database.windows.net/ --output json</c>).
    /// When set, the token is applied via <see cref="Microsoft.Data.SqlClient.SqlConnection.AccessToken"/>
    /// and <see cref="ConnectionString"/> must not carry inline auth (Authentication/User ID/Password).
    /// </summary>
    public string? TokenCommand { get; set; }
}
