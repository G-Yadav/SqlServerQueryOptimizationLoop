using AzureSqlMcp.Application;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureSqlMcp.Presentation;

[McpServerToolType]
public class SpDeploymentTools(ISpDeploymentRepository repo)
{
    [McpServerTool, Description("Deploys a stored procedure to the database. The SQL must start with ALTER PROCEDURE. DELETE statements are only allowed against temp tables (#...) or table variables (@...), never persistent tables.")]
    public async Task<string> DeploySp(
        [Description("The full T-SQL ALTER PROCEDURE statement to execute.")] string sql,
        CancellationToken ct = default)
    {
        try
        {
            await repo.DeployAsync(sql, ct);
            return "Stored procedure deployed successfully.";
        }
        catch (ArgumentException ex) { return $"Invalid SQL: {ex.Message}"; }
        catch (Exception ex)         { return $"Deployment failed: {ex.Message}"; }
    }
}
