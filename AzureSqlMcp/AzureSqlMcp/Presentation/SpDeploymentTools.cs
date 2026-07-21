using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureSqlMcp;

[McpServerToolType]
public class SpDeploymentTools(ISpDeploymentRepository repo)
{
    [McpServerTool, Description("Deploys a stored procedure to the database. The SQL must start with ALTER PROCEDURE or CREATE OR ALTER PROCEDURE.")]
    public async Task<string> DeploySp(
        [Description("The full T-SQL ALTER PROCEDURE or CREATE OR ALTER PROCEDURE statement to execute.")] string sql)
    {
        try
        {
            await repo.DeployAsync(sql);
            return "Stored procedure deployed successfully.";
        }
        catch (ArgumentException ex) { return $"Invalid SQL: {ex.Message}"; }
        catch (Exception ex)         { return $"Deployment failed: {ex.Message}"; }
    }
}
