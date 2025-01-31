using DurableDemo.Trial;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DurableDemo;

public class Example1
{
    private const string Prefix = "Example1_";
    public const string Start = Prefix + nameof(Start);
    public const string Orchestrator = Prefix + nameof(Orchestrator);
    public const string ProvisionInfrastructure = Prefix + nameof(ProvisionInfrastructure);
    public const string SeedData = Prefix + nameof(SeedData);
    public const string DeployApplication = Prefix + nameof(DeployApplication);

    [Function(Start)]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "example1")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var logger = ctx.GetLogger<Example1>();
        var customer = await req.ReadFromJsonAsync<CustomerInformation>();

        if (customer is null)
        {
            return req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
        }

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(Orchestrator, customer);

        logger.LogInformation("Started orchestration '{InstanceId}' for customer {CustomerName}", instanceId, customer.Name);

        // DO NOT use this for public facing APIs. Instead use a custom status endpoint.
        // Built-in status check response includes api key which can be used for accessing all orchestration instances.
        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    [Function(Orchestrator)]
    public static async Task<Uri> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        CustomerInformation customer)
    {
        var logger = context.CreateReplaySafeLogger<Example1>();
        logger.LogInformation("Starting trial workflow for customer {CustomerName}", customer.Name);

        // Deploy infrastructure
        var infrastructure = await context.CallActivityAsync<CustomerInfrastructure>(ProvisionInfrastructure, customer);

        // Seed default data
        await context.CallActivityAsync(SeedData, infrastructure);

        // Deploy application
        await context.CallActivityAsync<string>(DeployApplication, infrastructure);

        return infrastructure.Uri;
    }

    [Function(ProvisionInfrastructure)]
    public static async Task<CustomerInfrastructure> ProvisionInfrastructureActivity(
        [ActivityTrigger] CustomerInformation customer,
        FunctionContext ctx)
    {
        var infrastructureProvisioner = ctx.InstanceServices.GetRequiredService<InfrastructureProvisioner>();
        return await infrastructureProvisioner.Provision(customer);
    }

    [Function(SeedData)]
    public static async Task SeedDataActivity(
        [ActivityTrigger] CustomerInfrastructure infrastructure,
        FunctionContext ctx)
    {
        var dataSeeder = ctx.InstanceServices.GetRequiredService<DefaultDataSeeder>();
        await dataSeeder.SeedDefaultData(infrastructure);
    }

    [Function(DeployApplication)]
    public static async Task DeployApplicationActivity(
        [ActivityTrigger] CustomerInfrastructure infrastructure,
        FunctionContext ctx)
    {
        var appDeployer = ctx.InstanceServices.GetRequiredService<AppDeployer>();
        await appDeployer.Deploy(infrastructure);
    }
}