using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DurableDemo;

public class Example4
{
    private const string Prefix = "Example4_";
    public const string Start = Prefix + nameof(Start);
    public const string Orchestrator = Prefix + nameof(Orchestrator);

    private record StartInfo(int Count);

    [Function(Start)]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Example4")]
        HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var startInfo = await req.ReadFromJsonAsync<StartInfo>();

        if (startInfo is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        for (var i = 0; i < startInfo.Count; i++)
        {
            await client.ScheduleNewOrchestrationInstanceAsync(Orchestrator);
        }

        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function(Orchestrator)]
    public static async Task RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger<Example4>();
        var outputs = new List<string>();

        await using (var _ = await context.AcquireSemaphorePermitAsync())
        {
            outputs.Add(await context.CallActivityAsync<string>(nameof(HelloActivity), "Tokyo"));
            outputs.Add(await context.CallActivityAsync<string>(nameof(HelloActivity), "Seattle"));
            outputs.Add(await context.CallActivityAsync<string>(nameof(HelloActivity), "London"));

            await context.CreateTimer(TimeSpan.FromSeconds(5), CancellationToken.None);
        }

        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        logger.LogInformation( "{Timestamp}: Hello from instance {Instance}, with data: {Data}",
            context.CurrentUtcDateTime,
            context.InstanceId,
            string.Join( ", ", outputs ) );
    }

    [Function( nameof( HelloActivity ) )]
    public static Task<string> HelloActivity( [ActivityTrigger] string input )
    {
        return Task.FromResult( "Hello " + input + "!" );
    }
}

public sealed class OrchestratorSemaphoreState
{
    public HashSet<string> Running { get; set; } = [];
    public Queue<string> Waiting { get; set; } = [];
}

public sealed class OrchestratorSemaphoreSettings
{
    public static OrchestratorSemaphoreSettings Default { get; } = new();

    public int MaxConcurrent { get; init; } = 15;
    public TimeSpan MaxLeaseTime { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed class OrchestratorSemaphore(
    IConfiguration configuration,
    DurableTaskClient client,
    ILogger<OrchestratorSemaphore> logger)
    : TaskEntity<OrchestratorSemaphoreState>
{
    private OrchestratorSemaphoreSettings Settings => configuration
        .GetSection(Context.Id.Name)
        .GetSection(Context.Id.Key)
        .Get<OrchestratorSemaphoreSettings>() ?? OrchestratorSemaphoreSettings.Default;

    private async Task RaiseAcquiredEvent( string instanceId ) =>
        await client.RaiseEventAsync(instanceId, "SemaphorePermitAcquired");

    private void ScheduleRelease( string instanceId, TimeSpan delay ) =>
        Context.SignalEntity(Context.Id, nameof(ReleasePermit), instanceId, new SignalEntityOptions
        {
            SignalTime = DateTimeOffset.UtcNow.Add(delay)
        });

    public async Task AcquirePermit( string instanceId )
    {
        logger.LogInformation("Acquiring permit for instance {InstanceId}", instanceId);

        var settings = Settings;

        // If the instance is already running, we can immediately acquire a permit from the semaphore
        if (State.Running.Contains(instanceId))
        {
            logger.LogInformation("Instance {InstanceId} is already running", instanceId);
            await RaiseAcquiredEvent(instanceId);
        }
        // If we have slots left, we can acquire the semaphore
        else if (State.Running.Count < settings.MaxConcurrent)
        {
            State.Running.Add(instanceId);
            ScheduleRelease(instanceId, settings.MaxLeaseTime);

            await RaiseAcquiredEvent(instanceId);

            logger.LogInformation("Instance {InstanceId} acquired permit. Permit count is {Count}", instanceId, State.Running.Count);
        }
        // Else put on the waiting queue
        else
        {
            State.Waiting.Enqueue(instanceId);

            logger.LogInformation("Instance {InstanceId} is waiting. Permit count is {Count}. Wait list is {WaitCount}",
                instanceId,
                State.Running.Count,
                State.Waiting.Count);
        }
    }

    public async Task ReleasePermit( string instanceId )
    {
        logger.LogInformation("Releasing permit for instance {InstanceId}", instanceId);
        var settings = Settings;

        // If the instance isn't running, just exit.
        // Unfortunately, it isn't possible to cancel a scheduled signal,
        // the automatic release will happen anyway even though it has been release manually (via disposal of the slot).
        // See issue: https://github.com/Azure/azure-functions-durable-extension/issues/1455
        if (!State.Running.Remove(instanceId))
        {
            logger.LogDebug("Instance {InstanceId} is not running", instanceId);
            return;
        }

        // If we have slots left, we can pop one from the waiting queue and acquire the semaphore for it.
        if (State.Running.Count < settings.MaxConcurrent
            && State.Waiting.TryDequeue(out var waitingInstanceId))
        {
            State.Running.Add(waitingInstanceId);
            ScheduleRelease(waitingInstanceId, settings.MaxLeaseTime);

            await RaiseAcquiredEvent(waitingInstanceId);

            logger.LogInformation("Instance {InstanceId} acquired permit. Permit count is {Count}. Wait list is {WaitCount}",
                waitingInstanceId,
                State.Running.Count,
                State.Waiting.Count);
        }
    }

    [Function( nameof( OrchestratorSemaphore ) )]
    public static Task Run(
        [EntityTrigger] TaskEntityDispatcher dispatcher,
        [DurableClient] DurableTaskClient client,
        FunctionContext context )
    {
        var configuration = context.InstanceServices.GetRequiredService<IConfiguration>();
        var logger = context.GetLogger<OrchestratorSemaphore>();
        var semaphore = new OrchestratorSemaphore( configuration, client, logger );

        return dispatcher.DispatchAsync(semaphore);
    }
}

public static class SemaphoreExtensions
{
    public static async Task<IAsyncDisposable> AcquireSemaphorePermitAsync(
        this TaskOrchestrationContext context,
        string? key = null)
    {
        await context.SignalSemaphoreAsync(nameof(OrchestratorSemaphore.AcquirePermit), key);
        await context.WaitForExternalEvent<object>( "SemaphorePermitAcquired" );

        return new SemaphorePermit(context, key);
    }

    public static async Task ReleaseSemaphorePermitAsync(
        this TaskOrchestrationContext context,
        string? key = null)
    {
        await context.SignalSemaphoreAsync(nameof(OrchestratorSemaphore.ReleasePermit), key);
    }

    private static async Task SignalSemaphoreAsync(
        this TaskOrchestrationContext context,
        string operationName,
        string? key = null)
    {
        key ??= context.Name;

        await context.Entities.SignalEntityAsync(
            new EntityInstanceId( nameof( OrchestratorSemaphore ), key ),
            operationName,
            context.InstanceId );
    }

    private class SemaphorePermit(TaskOrchestrationContext context, string? key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await ReleaseSemaphorePermitAsync(context, key);
    }
}