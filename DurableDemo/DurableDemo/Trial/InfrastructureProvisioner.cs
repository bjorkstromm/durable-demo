using Microsoft.Extensions.Logging;

namespace DurableDemo.Trial;

public class InfrastructureProvisioner(ILogger<InfrastructureProvisioner> logger)
{
    public async Task<CustomerInfrastructure> Provision(CustomerInformation customer)
    {
        logger.LogInformation("Provisioning infrastructure for customer {CustomerName}", customer.Name);

        #region Implementation
        await Task.Delay(5000);
        #endregion

        return new CustomerInfrastructure
        {
            Customer = customer
        };
    }

    public Task StartProvision(CustomerInformation customer)
    {
        logger.LogInformation("Provisioning infrastructure for customer {CustomerName}", customer.Name);

        return Task.CompletedTask;
    }
}