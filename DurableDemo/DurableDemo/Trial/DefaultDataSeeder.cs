using Microsoft.Extensions.Logging;

namespace DurableDemo.Trial;

public class DefaultDataSeeder(ILogger<DefaultDataSeeder> logger)
{
    public async Task SeedDefaultData(CustomerInfrastructure customer)
    {
        logger.LogInformation("Seeding default data for customer {CustomerName}", customer.Customer.Name);

        #region Implementation
        await Task.Delay(5000);
        #endregion
    }
}