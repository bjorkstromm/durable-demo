using DurableDemo.Trial;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddScoped<InfrastructureProvisioner>();
        services.AddScoped<DefaultDataSeeder>();
        services.AddScoped<AppDeployer>();
        services.AddScoped<Crm>();
    })
    .ConfigureAppConfiguration( builder =>
    {
        builder.AddJsonFile( "appsettings.json", optional: true, reloadOnChange: true );
        builder.AddEnvironmentVariables();
    } )
    .ConfigureLogging( ( ctx, logging ) =>
    {
        logging.Services.Configure<LoggerFilterOptions>( options =>
        {
            var defaultRule = options.Rules
                .FirstOrDefault( rule =>
                    string.Equals(
                        rule.ProviderName,
                        "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider" ) );

            if( defaultRule is not null )
            {
                options.Rules.Remove( defaultRule );
            }
        } );

        logging.AddConfiguration( ctx.Configuration.GetSection( "Logging" ) );
    } )
    .Build();

host.Run();