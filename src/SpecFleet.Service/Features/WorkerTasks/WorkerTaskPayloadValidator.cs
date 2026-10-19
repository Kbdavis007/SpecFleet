using System.Text.Json;
using SpecFleet.Service.Features.TaskOrchestration;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Service.Features.WorkerTasks;

/// <summary>
/// Validates typed worker task payloads for each supported SpecFleet category. A task is only
/// dispatchable when its category is configured and its payload satisfies that category's
/// required fields.
/// </summary>
public sealed class WorkerTaskPayloadValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<WorkerTaskType, string[]> RequiredFields =
        new Dictionary<WorkerTaskType, string[]>
        {
            [WorkerTaskType.GenerateWorker] = ["name", "language"],
            [WorkerTaskType.GenerateUiPage] = ["name", "route"],
            [WorkerTaskType.GenerateTerraformModule] = ["name", "provider"],
            [WorkerTaskType.GenerateCloudflareTunnelConfig] = ["tunnelName", "hostname"],
            [WorkerTaskType.GenerateRabbitMqConfig] = ["virtualHost", "queues"],
            [WorkerTaskType.GenerateControlPlaneLogic] = ["name", "trigger"]
        };

    public static IReadOnlyCollection<WorkerTaskType> SupportedCategories =>
        RequiredFields.Keys.ToArray();

    public bool IsSupported(WorkerTaskType taskType) => RequiredFields.ContainsKey(taskType);

    public ServiceResult<WorkerTaskPreview> Preview(WorkerTaskType taskType, string payload)
    {
        if (!IsSupported(taskType))
        {
            return ServiceResult<WorkerTaskPreview>.FromError(
                new ServiceError(
                    ServiceErrorCode.Validation,
                    $"The worker category '{taskType}' is not configured."));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Validation("A task payload is required.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return Validation("The task payload must be valid JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return Validation("The task payload must be a JSON object.");
            }

            var missing = RequiredFields[taskType]
                .Where(field => !HasValue(document.RootElement, field))
                .ToArray();
            if (missing.Length > 0)
            {
                return Validation(
                    $"The '{taskType}' payload is missing required field(s): {string.Join(", ", missing)}.");
            }
        }

        var digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
        return ServiceResult<WorkerTaskPreview>.FromValue(
            new WorkerTaskPreview(taskType, payload, digest));
    }

    private static bool HasValue(JsonElement element, string field) =>
        element.TryGetProperty(field, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
        (value.ValueKind is not JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString())) &&
        (value.ValueKind is not JsonValueKind.Array || value.GetArrayLength() > 0);

    private static ServiceResult<WorkerTaskPreview> Validation(string message) =>
        ServiceResult<WorkerTaskPreview>.FromError(new ServiceError(ServiceErrorCode.Validation, message));
}

public sealed record WorkerTaskPreview(WorkerTaskType TaskType, string Payload, string PayloadDigest);
