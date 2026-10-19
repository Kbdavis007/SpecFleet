using SpecFleet.Service.Features.TaskOrchestration;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.WorkerTasks;

/// <summary>
/// Executes a leased worker task envelope through Service-owned category use cases. The
/// consumer in Workers only polls, invokes this, and acknowledges; all task behavior and
/// validation live here.
/// </summary>
public sealed class WorkerTaskExecutor(WorkerTaskPayloadValidator validator)
{
    public ServiceResult<WorkerTaskExecution> Execute(string envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return Failure("The task envelope is empty.");
        }

        WorkerTaskEnvelope? envelope;
        try
        {
            envelope = System.Text.Json.JsonSerializer.Deserialize<WorkerTaskEnvelope>(
                envelopeJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
        catch (System.Text.Json.JsonException)
        {
            return Failure("The task envelope could not be parsed.");
        }

        if (envelope is null)
        {
            return Failure("The task envelope could not be parsed.");
        }

        if (!Enum.TryParse<WorkerTaskType>(envelope.TaskType, ignoreCase: true, out var taskType) ||
            !validator.IsSupported(taskType))
        {
            return Failure($"The worker category '{envelope.TaskType}' is not configured.");
        }

        var payload = envelope.Payload;
        var preview = validator.Preview(taskType, payload);
        if (preview is ServiceResult<WorkerTaskPreview>.Failure failure)
        {
            return ServiceResult<WorkerTaskExecution>.FromError(failure.Error);
        }

        var previewValue = ((ServiceResult<WorkerTaskPreview>.Success)preview).Value;
        if (!string.Equals(previewValue.PayloadDigest, envelope.PayloadDigest, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("The task payload no longer matches the approved preview.");
        }

        return ServiceResult<WorkerTaskExecution>.FromValue(
            new WorkerTaskExecution(taskType, payload, previewValue.PayloadDigest));
    }

    private static ServiceResult<WorkerTaskExecution> Failure(string message) =>
        ServiceResult<WorkerTaskExecution>.FromError(new ServiceError(ServiceErrorCode.Validation, message));
}

public sealed record WorkerTaskExecution(WorkerTaskType TaskType, string Payload, string PayloadDigest);
