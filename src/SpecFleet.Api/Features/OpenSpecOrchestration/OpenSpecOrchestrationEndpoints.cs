using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SpecFleet.Api.Features.Shared;
using SpecFleet.Service.Features.OpenSpecOrchestration;
using SpecFleet.Service.Features.WorkerTasks;
using SpecFleet.Service.Shared.Results;

namespace SpecFleet.Api.Features.OpenSpecOrchestration;

/// <summary>
/// Minimal API transport for OpenSpec operations and worker task orchestration. Handlers bind
/// input and delegate to Service use cases; no orchestration logic lives here.
/// </summary>
public static class OpenSpecOrchestrationEndpoints
{
    public static IEndpointRouteBuilder MapOpenSpecOrchestrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/orchestration").RequireAuthorization();

        group.MapPost(
            "/openspec/operations",
            async (
                HttpContext context,
                OpenSpecOperationRequestBody request,
                OpenSpecOperationHandler handler,
                CancellationToken cancellationToken) =>
            {
                var actor = ResolveActor(context);
                if (string.IsNullOrWhiteSpace(actor))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: nameof(ServiceErrorCode.Unauthorized),
                        detail: "An authenticated user is required.");
                }

                if (!Enum.TryParse<OpenSpecOperationKind>(request.Operation, ignoreCase: true, out var operation))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: nameof(ServiceErrorCode.Validation),
                        detail: "The requested command is not supported.");
                }

                var result = await handler.ExecuteAsync(
                    new OpenSpecOperationRequest(
                        actor,
                        request.WorkspacePath ?? string.Empty,
                        operation,
                        request.Target,
                        request.Confirmed),
                    cancellationToken);

                return ServiceResultHttpMapper.ToHttpResult(result);
            });

        group.MapPost(
            "/worker-tasks",
            async (
                HttpContext context,
                WorkerTaskDispatchRequestBody request,
                WorkerTaskHandler handler,
                CancellationToken cancellationToken) =>
            {
                var actor = ResolveActor(context);
                if (string.IsNullOrWhiteSpace(actor))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: nameof(ServiceErrorCode.Unauthorized),
                        detail: "An authenticated user is required.");
                }

                if (!Enum.TryParse<Service.Features.TaskOrchestration.WorkerTaskType>(
                        request.TaskType,
                        ignoreCase: true,
                        out var taskType))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: nameof(ServiceErrorCode.Validation),
                        detail: "A supported worker task type is required.");
                }

                var result = await handler.DispatchAsync(
                    taskType,
                    request.Payload ?? string.Empty,
                    request.IdempotencyKey ?? $"api-{Guid.NewGuid():N}",
                    new Service.Features.TaskOrchestration.WorkerTaskApproval(
                        actor,
                        request.ApprovalReference ?? string.Empty,
                        taskType,
                        request.PayloadDigest ?? string.Empty,
                        request.Target ?? string.Empty),
                    cancellationToken);

                return ServiceResultHttpMapper.ToHttpResult(result);
            });

        group.MapGet(
            "/worker-tasks/{taskId:guid}",
            async (Guid taskId, WorkerTaskHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.GetStatusAsync(taskId, cancellationToken);
                return ServiceResultHttpMapper.ToHttpResult(result);
            });

        return endpoints;
    }

    private static string ResolveActor(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.Identity?.Name
        ?? string.Empty;
}

public sealed record OpenSpecOperationRequestBody(
    string Operation,
    string? WorkspacePath,
    string? Target,
    bool Confirmed);

public sealed record WorkerTaskDispatchRequestBody(
    string TaskType,
    string? Payload,
    string? PayloadDigest,
    string? IdempotencyKey,
    string? ApprovalReference,
    string? Target);
