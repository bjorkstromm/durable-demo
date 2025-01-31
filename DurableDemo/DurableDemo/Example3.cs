using System.Net;
using DurableDemo.Trial;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DurableDemo;

public class Example3
{
    private const string Prefix = "Example3_";
    public const string Start = Prefix + nameof(Start);
    public const string Stop = Prefix + nameof(Stop);
    public const string Orchestrator = Prefix + nameof(Orchestrator);
    public const string GetExpiredTrials = Prefix + nameof(GetExpiredTrials);
    public const string CleanupInfrastructure = Prefix + nameof(CleanupInfrastructure);
    public const string InstanceId = Prefix + "Cleanup";

    [Function(Start)]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Example3")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var logger = ctx.GetLogger<Example3>();

        var instance = await client.GetInstanceAsync(InstanceId);

        if (instance?.RuntimeStatus == OrchestrationRuntimeStatus.Running)
        {
            logger.LogInformation("Cleanup '{InstanceId}' is already running", InstanceId);

            // DO NOT use this for public facing APIs. Instead use a custom status endpoint.
            // Built-in status check response includes api key which can be used for accessing all orchestration instances.
            return await client.CreateCheckStatusResponseAsync(req, instance.InstanceId);
        }

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(Orchestrator,
            new StartOrchestrationOptions(InstanceId));

        logger.LogInformation("Started cleanup '{InstanceId}'", instanceId);

        // DO NOT use this for public facing APIs.
        // Built-in status check response includes api key which can be used for accessing all orchestration instances.
        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    [Function(Stop)]
    public static async Task<HttpResponseData> StopOrchestrator(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Example3/stop")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var logger = ctx.GetLogger<Example3>();

        logger.LogInformation("Terminating cleanup '{InstanceId}'", InstanceId);

        await client.TerminateInstanceAsync(InstanceId, "User requested termination");
        await client.WaitForInstanceCompletionAsync(InstanceId);

        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function(Orchestrator)]
    public static async Task RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger(nameof(Example3));
        logger.LogInformation("Starting trial cleanup workflow");

        var expiredTrials = await context.CallActivityAsync<CustomerInformation[]>(GetExpiredTrials);

        logger.LogInformation("Found {Count} expired trials", expiredTrials.Length);

        var tasks = new List<Task>();

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var expiredTrial in expiredTrials)
        {
            tasks.Add(context.CallActivityAsync(CleanupInfrastructure, expiredTrial));
        }

        await Task.WhenAll(tasks);

        await context.CreateTimer(TimeSpan.FromSeconds(20), CancellationToken.None);

        context.ContinueAsNew();
    }

    [Function(GetExpiredTrials)]
    public static async Task<CustomerInformation[]> GetExpiredTrialsActivity(
        [ActivityTrigger] FunctionContext ctx)
    {
        var crm = ctx.InstanceServices.GetRequiredService<Crm>();
        return await crm.GetExpiredTrials();
    }

    [Function(CleanupInfrastructure)]
    public static async Task CleanupInfrastructureActivity(
        [ActivityTrigger] CustomerInformation customer,
        FunctionContext ctx)
    {
        var infrastructure = ctx.InstanceServices.GetRequiredService<InfrastructureProvisioner>();
        await infrastructure.RemoveInfrastructure(customer);
    }
}