namespace AzureSqlMcp.Application;

public interface ISpDeploymentRepository
{
    Task DeployAsync(string sql);
}
