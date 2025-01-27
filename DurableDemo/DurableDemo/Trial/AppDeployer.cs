using Microsoft.Extensions.Logging;

namespace DurableDemo.Trial;

public class AppDeployer(ILogger<AppDeployer> logger)
{
    public async Task Deploy(CustomerInfrastructure infrastructure)
    {
        logger.LogInformation("Deploying application for customer {CustomerName}", infrastructure.Customer.Name);

        #region Implementation
        await Task.Delay(5000);
        #endregion
    }

    public Task StartDeploy(CustomerInfrastructure infrastructure)
    {
        logger.LogInformation("Deploying application for customer {CustomerName}", infrastructure.Customer.Name);

        return Task.CompletedTask;
    }
}