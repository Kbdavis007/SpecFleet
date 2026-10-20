using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpecFleet.Test.Support;

namespace SpecFleet.Test.Features.OpenSpecOrchestration;

/// <summary>
/// Verifies the OpenSpec and worker orchestration Minimal API transport end to end against a
/// genuinely running API process, the real OpenSpec CLI, and the shared real PostgreSQL
/// container.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OpenSpecOrchestrationEndpointsTests
{
    [Fact]
    public async Task Orchestration_endpoints_require_authentication()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(state.ConnectionString);

        using var anonymous = new HttpClient { BaseAddress = new Uri(api.BaseUrl) };
        using var response = await anonymous.PostAsJsonAsync("/api/orchestration/openspec/operations", new
        {
            operation = "List",
            workspacePath = "/tmp",
            confirmed = true
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_openspec_operation_runs_through_the_running_api()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            openSpecWorkspaceRoot: RepositoryRoot());

        using var response = await api.Client.PostAsJsonAsync("/api/orchestration/openspec/operations", new
        {
            operation = "Validate",
            workspacePath = RepositoryRoot(),
            target = "add-ai-chatbot-github-integration",
            confirmed = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("succeeded").GetBoolean());
    }

    [Fact]
    public async Task Unsupported_command_is_rejected_and_executes_nothing()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(
            state.ConnectionString,
            openSpecWorkspaceRoot: RepositoryRoot());

        using var response = await api.Client.PostAsJsonAsync("/api/orchestration/openspec/operations", new
        {
            operation = "DeleteEverything",
            workspacePath = RepositoryRoot(),
            target = "list; rm -rf /",
            confirmed = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Worker_task_dispatch_and_status_round_trip_through_the_running_api()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(state.ConnectionString);

        const string payload = """{"name":"api-worker","language":"csharp"}""";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        using var dispatch = await api.Client.PostAsJsonAsync("/api/orchestration/worker-tasks", new
        {
            taskType = "GenerateWorker",
            payload,
            payloadDigest = digest,
            idempotencyKey = $"api-{Guid.NewGuid():N}",
            approvalReference = "approval-reference",
            target = "api-worker"
        });

        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        using var document = JsonDocument.Parse(await dispatch.Content.ReadAsStringAsync());
        var taskId = document.RootElement.GetProperty("taskId").GetGuid();

        using var status = await api.Client.GetAsync($"/api/orchestration/worker-tasks/{taskId}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var statusDocument = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal("Queued", statusDocument.RootElement.GetProperty("status").GetString());
        Assert.True(statusDocument.RootElement.GetProperty("inProgress").GetBoolean());
    }

    [Fact]
    public async Task Invalid_worker_payload_is_rejected_by_the_running_api()
    {
        var state = await SharedPostgreSql.InstanceAsync();
        await state.ResetAsync();
        await using var api = await ApiProcessHarness.StartAsync(state.ConnectionString);

        using var response = await api.Client.PostAsJsonAsync("/api/orchestration/worker-tasks", new
        {
            taskType = "GenerateWorker",
            payload = """{"name":"missing-language"}""",
            payloadDigest = "irrelevant",
            idempotencyKey = $"api-{Guid.NewGuid():N}",
            approvalReference = "approval-reference",
            target = "missing-language"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpecFleet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
