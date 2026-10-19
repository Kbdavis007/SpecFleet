using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Features.WorkerTasks;
using SpecFleet.Workers.Consumers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

var connectionString = builder.Configuration.GetConnectionString("SpecFleet")
    ?? builder.Configuration["SpecFleet:ConnectionString"]
    ?? string.Empty;

// The bootstrap consumer is thin transport: it polls the Service-owned durable queue and
// delegates every task to the Service-owned executor. All task behavior lives in Service.
builder.Services.AddSingleton(new PostgreSqlTaskQueue(connectionString));
builder.Services.AddSingleton<WorkerTaskPayloadValidator>();
builder.Services.AddSingleton<WorkerTaskExecutor>();
builder.Services.AddSingleton(services => new BootstrapWorkerConsumer(
    services.GetRequiredService<PostgreSqlTaskQueue>(),
    services.GetRequiredService<WorkerTaskExecutor>()));
builder.Services.AddHostedService<BootstrapWorkerHostedService>();

var host = builder.Build();

await host.Services.GetRequiredService<PostgreSqlTaskQueue>().InitializeAsync();

host.Run();

/// <summary>
/// Hosted wrapper that starts and stops the bootstrap consumer with the host lifetime.
/// </summary>
internal sealed class BootstrapWorkerHostedService(BootstrapWorkerConsumer consumer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => consumer.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => consumer.StopAsync(cancellationToken);
}
