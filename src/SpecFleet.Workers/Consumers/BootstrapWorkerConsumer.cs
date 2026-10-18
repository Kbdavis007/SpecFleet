using System.Text.Json;
using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Workers.Consumers;

public sealed class BootstrapWorkerConsumer : IDisposable, IAsyncDisposable
{
    private readonly PostgreSqlTaskQueue _queue;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _workerId = $"bootstrap-worker-{Guid.NewGuid():N}";
    private Task? _processingLoop;

    public BootstrapWorkerConsumer(PostgreSqlTaskQueue queue, TimeSpan? pollInterval = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
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
        var parseResult = TryParseEnvelope(lease.Payload, out var envelope);
        if (!parseResult)
        {
            await RetryAsync(lease, TimeSpan.FromSeconds(1), "The task payload could not be parsed.", cancellationToken);
            return WorkerDisposition.Retry;
        }

        if (string.Equals(envelope.TaskType, "generate-worker", StringComparison.OrdinalIgnoreCase) && envelope.ShouldFail)
        {
            await RetryAsync(lease, TimeSpan.FromSeconds(1), "Simulated worker failure.", cancellationToken);
            return WorkerDisposition.Retry;
        }

        var completion = await _queue.CompleteAsync(lease, cancellationToken);
        if (completion is ServiceResult<bool>.Failure failure)
        {
            if (failure.Error.IsRetryable)
            {
                await RetryAsync(lease, TimeSpan.FromSeconds(1), failure.Error.Message, cancellationToken);
                return WorkerDisposition.Retry;
            }

            await RetryAsync(lease, TimeSpan.FromSeconds(1), failure.Error.Message, cancellationToken);
            return WorkerDisposition.Reject;
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

    private static bool TryParseEnvelope(string payload, out BootstrapTaskEnvelope envelope)
    {
        envelope = default!;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("taskType", out var taskTypeElement) ||
                !root.TryGetProperty("payload", out var payloadElement) ||
                taskTypeElement.ValueKind is not JsonValueKind.String)
            {
                return false;
            }

            envelope = new BootstrapTaskEnvelope(
                taskTypeElement.GetString() ?? string.Empty,
                payloadElement,
                root.TryGetProperty("shouldFail", out var shouldFailElement) &&
                shouldFailElement.ValueKind == JsonValueKind.True);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record BootstrapTaskEnvelope(string TaskType, JsonElement Payload, bool ShouldFail);
}
