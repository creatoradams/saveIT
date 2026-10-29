using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaveIT.Search;

var builder = Host.CreateApplicationBuilder(args);
var connection = builder.Configuration.GetConnectionString("SearchWorker")
    ?? throw new InvalidOperationException("ConnectionStrings:SearchWorker is required; never use an owner connection.");
var configured = builder.Configuration.GetSection("SearchWorker:Tenants").Get<string[]>() ?? [];
if (configured.Length == 0 || configured.Length > 1000 || configured.Any(x => !Guid.TryParse(x, out var id) || id == Guid.Empty))
    throw new InvalidOperationException("Configure an explicit, bounded list of SearchWorker:Tenants.");
var tenants = configured.Select(Guid.Parse).Distinct().ToArray();
builder.Services.AddSingleton(new SearchProcessor(connection));
builder.Services.AddSingleton(tenants);
if (args.Contains("--once") || args.Contains("--rebuild") || builder.Configuration["replay-event"] is not null)
{
    using var host = builder.Build();
    var processor = host.Services.GetRequiredService<SearchProcessor>();
    foreach (var tenant in tenants)
    {
        if (args.Contains("--rebuild")) Console.WriteLine($"Search rebuild completed: {await processor.RebuildAsync(tenant)} records; tenant {tenant}.");
        if (builder.Configuration["replay-event"] is string eventText)
        {
            if (!Guid.TryParse(eventText, out var eventId)) throw new InvalidOperationException("replay-event must be an event UUID.");
            Console.WriteLine($"Search receipt reset: {await processor.ReplayAsync(tenant, eventId)}; tenant {tenant}.");
        }
        var result = await processor.ProcessBatchAsync(tenant);
        Console.WriteLine($"Search batch: {result.Completed} completed, {result.Retried} retries, {result.DeadLettered} dead letters; tenant {tenant}.");
    }
    return;
}
builder.Services.AddHostedService<SearchWorker>();
await builder.Build().RunAsync();

internal sealed class SearchWorker(SearchProcessor processor, Guid[] tenants, ILogger<SearchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Refuse unsafe roles before entering the background retry loop.
        await processor.ValidateAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var tenant in tenants)
            {
                try
                {
                    var result = await processor.ProcessBatchAsync(tenant, stoppingToken);
                    if (result.Completed + result.Retried + result.DeadLettered > 0)
                        logger.LogInformation("Search tenant {Tenant}: {Completed} completed, {Retried} retried, {Dead} dead letters", tenant, result.Completed, result.Retried, result.DeadLettered);
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    // Do not log exception text, SQL, search terms or document contents.
                    logger.LogWarning("Search processing unavailable: {ErrorType}; tenant {Tenant}", error.GetType().Name, tenant);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
