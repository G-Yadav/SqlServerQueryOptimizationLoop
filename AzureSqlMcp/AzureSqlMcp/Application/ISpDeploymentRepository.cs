namespace AzureSqlMcp;

public interface ISpDeploymentRepository
{
    Task DeployAsync(string sql);
}
