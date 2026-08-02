namespace AzureSqlMcp.Application;

/// <summary>
/// Supplies an Entra ID access token for opening a SQL connection, or <c>null</c> when the server
/// is configured to authenticate via the connection string alone (no token mode).
/// </summary>
public interface IAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
}
