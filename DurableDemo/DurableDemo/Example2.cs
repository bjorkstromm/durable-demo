using System.Net;
using DurableDemo.Trial;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DurableDemo;

public class Example2
{
    private const string Prefix = "Example2_";
    public const string Start = Prefix + nameof(Start);
    public const string Orchestrator = Prefix + nameof(Orchestrator);
    public const string StartProvisionInfrastructure = Prefix + nameof(StartProvisionInfrastructure);
    public const string InfrastructureProvisioned = Prefix + nameof(InfrastructureProvisioned);
    public const string SeedData = Prefix + nameof(SeedData);
    public const string StartDeployApplication = Prefix + nameof(StartDeployApplication);
    public const string ApplicationDeployed = Prefix + nameof(ApplicationDeployed);

    [Function(Start)]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "example2")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var logger = ctx.GetLogger<Example2>();
        var customer = await req.ReadFromJsonAsync<CustomerInformation>();

        if (customer is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(Orchestrator, customer);

        logger.LogInformation("Started orchestration '{InstanceId}' for customer {CustomerName}", instanceId, customer.Name);

        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    [Function(InfrastructureProvisioned)]
    public static async Task<HttpResponseData> OnInfrastructureProvisioned(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "example2/{instanceId}/infrastructure")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string instanceId,
        FunctionContext ctx)
    {
        var logger = ctx.GetLogger<Example2>();
        var customer = await req.ReadFromJsonAsync<CustomerInformation>();

        if (customer is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        logger.LogInformation("Raising infrastructure provisioned event for '{InstanceId}' and customer {CustomerName}", instanceId, customer.Name);
        await client.RaiseEventAsync(instanceId, InfrastructureProvisioned, new CustomerInfrastructure { Customer = customer });

        return req.CreateResponse(HttpStatusCode.OK);
    }

    [Function(ApplicationDeployed)]
    public static async Task<HttpResponseData> OnApplicationDeployed(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "example2/{instanceId}/application")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string instanceId,
        FunctionContext ctx)
    {
        var logger = ctx.GetLogger<Example2>();

        logger.LogInformation("Raising application deployed event for '{InstanceId}'", instanceId);
        await client.RaiseEventAsync(instanceId, ApplicationDeployed );

        return req.CreateResponse(HttpStatusCode.OK);
    }

    [Function(Orchestrator)]
    public static async Task<Uri> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        CustomerInformation customer)
    {
        var logger = context.CreateReplaySafeLogger(nameof(Example2));
        logger.LogInformation("Starting trial workflow for customer {CustomerName}", customer.Name);

        // Deploy infrastructure
        await context.CallActivityAsync(StartProvisionInfrastructure, customer);
        context.SetCustomStatus("Provisioning infrastructure...");
        var infrastructure = await context.WaitForExternalEvent<CustomerInfrastructure>(InfrastructureProvisioned);

        // Seed default data
        context.SetCustomStatus("Seeding default data...");
        await context.CallActivityAsync(SeedData, infrastructure);

        // Deploy application
        await context.CallActivityAsync<string>(StartDeployApplication, infrastructure);
        context.SetCustomStatus("Deploying application...");
        await context.WaitForExternalEvent<object?>(ApplicationDeployed);

        context.SetCustomStatus("Workflow completed.");

        return infrastructure.Uri;
    }

    [Function(StartProvisionInfrastructure)]
    public static async Task ProvisionInfrastructureActivity(
        [ActivityTrigger] CustomerInformation customer,
        FunctionContext ctx)
    {
        var infrastructureProvisioner = ctx.InstanceServices.GetRequiredService<InfrastructureProvisioner>();
        await infrastructureProvisioner.StartProvision(customer);
    }

    [Function(SeedData)]
    public static async Task SeedDataActivity(
        [ActivityTrigger] CustomerInfrastructure infrastructure,
        FunctionContext ctx)
    {
        var dataSeeder = ctx.InstanceServices.GetRequiredService<DefaultDataSeeder>();
        await dataSeeder.SeedDefaultData(infrastructure);
    }

    [Function(StartDeployApplication)]
    public static async Task DeployApplicationActivity(
        [ActivityTrigger] CustomerInfrastructure infrastructure,
        FunctionContext ctx)
    {
        var appDeployer = ctx.InstanceServices.GetRequiredService<AppDeployer>();
        await appDeployer.StartDeploy(infrastructure);
    }
}