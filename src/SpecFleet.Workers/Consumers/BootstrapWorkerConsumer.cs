using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Features.WorkerTasks;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Workers.Consumers;

public sealed class BootstrapWorkerConsumer : IDisposable, IAsyncDisposable
{
    private readonly PostgreSqlTaskQueue _queue;
    private readonly WorkerTaskExecutor _executor;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _workerId = $"bootstrap-worker-{Guid.NewGuid():N}";
    private Task? _processingLoop;

    public BootstrapWorkerConsumer(
        PostgreSqlTaskQueue queue,
        WorkerTaskExecutor executor,
        TimeSpan? pollInterval = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        if (_pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "The poll interval must be positive.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _processingLoop ??= Task.Run(() => ProcessLoopAsync(_shutdown.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _shutdown.Cancel();

        if (_processingLoop is null)
        {
            return;
        }

        try
        {
            await _processingLoop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The worker is being stopped during a cancellation request. We already signal shutdown.
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _shutdown.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _shutdown.Dispose();
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var leaseResult = await _queue.LeaseNextAsync(_workerId, TimeSpan.FromSeconds(30), cancellationToken);
            if (leaseResult is not ServiceResult<TaskQueueLeaseAttempt>.Success { Value: TaskQueueLeaseAttempt.Leased leased })
            {
                await DelayAsync(cancellationToken);
                continue;
            }

            var disposition = await ExecuteAsync(leased.Lease, cancellationToken);
            if (disposition is null)
            {
                await DelayAsync(cancellationToken);
                continue;
            }

            await DelayAsync(cancellationToken);
        }
    }

    private async Task<WorkerDisposition?> ExecuteAsync(TaskQueueLease lease, CancellationToken cancellationToken)
    {
        // The consumer performs no task behavior of its own: it deserializes the durable
        // envelope, delegates to the Service-owned executor, and maps the typed result to an
        // acknowledgement, retry, or rejection.
        var execution = _executor.Execute(lease.Payload);
        if (execution is ServiceResult<WorkerTaskExecution>.Failure failure)
        {
            var disposition = failure.Error.IsRetryable ? WorkerDisposition.Retry : WorkerDisposition.Reject;
            await RetryAsync(lease, TimeSpan.FromSeconds(1), failure.Error.Message, cancellationToken);
            return disposition;
        }

        var completion = await _queue.CompleteAsync(lease, cancellationToken);
        if (completion is ServiceResult<bool>.Failure completionFailure)
        {
            await RetryAsync(lease, TimeSpan.FromSeconds(1), completionFailure.Error.Message, cancellationToken);
            return completionFailure.Error.IsRetryable ? WorkerDisposition.Retry : WorkerDisposition.Reject;
        }

        return WorkerDisposition.Acknowledge;
    }

    private async Task RetryAsync(
        TaskQueueLease lease,
        TimeSpan retryDelay,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var retryResult = await _queue.RetryAsync(lease, retryDelay, failureReason, cancellationToken);
        if (retryResult is ServiceResult<TaskQueueRetryResult>.Failure failure)
        {
            _ = await _queue.CompleteAsync(lease, cancellationToken);
        }
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_pollInterval, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown handled by outer loop.
        }
    }
}
