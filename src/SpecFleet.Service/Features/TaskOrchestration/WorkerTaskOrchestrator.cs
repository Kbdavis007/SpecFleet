using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpecFleet.Service.Features.TaskQueue;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.TaskOrchestration;

public sealed class WorkerTaskOrchestrator(PostgreSqlTaskQueue queue)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceResult<WorkerTaskDispatch>> DispatchAsync(
        WorkerTaskDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !Enum.IsDefined(request.TaskType))
        {
            return ValidationFailure("A supported worker task type is required.");
        }

        if (request.Approval is null ||
            string.IsNullOrWhiteSpace(request.Approval.ActorId) ||
            string.IsNullOrWhiteSpace(request.Approval.ApprovalReference))
        {
            return ServiceResult<WorkerTaskDispatch>.FromError(
                new ServiceError(ServiceErrorCode.Unauthorized, "An authenticated actor and explicit task approval are required."));
        }

        if (string.IsNullOrWhiteSpace(request.Approval.Target))
        {
            return ValidationFailure("An approved task target is required.");
        }

        if (request.Approval.TaskType != request.TaskType ||
            !MatchesApprovedPayload(request.Payload, request.Approval.PayloadDigest))
        {
            return ServiceResult<WorkerTaskDispatch>.FromError(
                new ServiceError(ServiceErrorCode.Conflict, "The task no longer matches the approved preview."));
        }

        if (!TryParseObject(request.Payload, out _))
        {
            return ValidationFailure("The task payload must be a JSON object.");
        }

        var taskType = GetTaskTypeName(request.TaskType);
        var envelope = JsonSerializer.Serialize(
            new WorkerTaskEnvelope(
                request.TaskType.ToString(),
                request.Payload,
                request.Approval.ActorId,
                request.Approval.ApprovalReference,
                request.Approval.Target,
                request.Approval.PayloadDigest),
            JsonOptions);
        var queueResult = await queue.EnqueueAsync(
            taskType,
            envelope,
            request.IdempotencyKey,
            cancellationToken: cancellationToken);

        return queueResult switch
        {
            ServiceResult<EnqueuedTask>.Success success =>
                ServiceResult<WorkerTaskDispatch>.FromValue(
                    new WorkerTaskDispatch(
                        success.Value.Id,
                        request.TaskType,
                        success.Value.Status,
                        request.Approval.ApprovalReference,
                        request.Approval.Target)),
            ServiceResult<EnqueuedTask>.Failure failure =>
                ServiceResult<WorkerTaskDispatch>.FromError(failure.Error),
            _ => ValidationFailure("The task queue returned an unsupported result.")
        };
    }

    private static string GetTaskTypeName(WorkerTaskType taskType) =>
        taskType switch
        {
            WorkerTaskType.GenerateWorker => "generate-worker",
            WorkerTaskType.GenerateUiPage => "generate-ui-page",
            WorkerTaskType.GenerateTerraformModule => "generate-terraform-module",
            WorkerTaskType.GenerateCloudflareTunnelConfig => "generate-cloudflare-tunnel-config",
            WorkerTaskType.GenerateRabbitMqConfig => "generate-rabbitmq-config",
            WorkerTaskType.GenerateControlPlaneLogic => "generate-control-plane-logic",
            _ => string.Empty
        };

    private static bool TryParseObject(string? payload, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return false;
            }

            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool MatchesApprovedPayload(string payload, string? approvedDigest)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(approvedDigest))
        {
            return false;
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(digest),
            Encoding.ASCII.GetBytes(approvedDigest.ToUpperInvariant()));
    }

    private static ServiceResult<WorkerTaskDispatch> ValidationFailure(string message) =>
        ServiceResult<WorkerTaskDispatch>.FromError(
            new ServiceError(ServiceErrorCode.Validation, message));
}

/// <summary>
/// The durable queue envelope for an approved worker task. The payload is carried as the exact
/// approved JSON string so the approved digest still matches after JSONB round-tripping, which
/// normalizes object key order. The consumer deserializes this to invoke the Service executor.
/// </summary>
public sealed record WorkerTaskEnvelope(
    string TaskType,
    string Payload,
    string ApprovedBy,
    string ApprovalReference,
    string Target,
    string PayloadDigest);
