using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace SpecFleet.Test.Support;

/// <summary>
/// Starts the real SpecFleet.Api as a separate process (a genuinely running API, not an
/// in-memory test host) against the shared real PostgreSQL container, and exposes an
/// authenticated HttpClient. Used to verify HTTP and streaming mappings end to end.
/// </summary>
public sealed class ApiProcessHarness : IAsyncDisposable
{
    private readonly Process _process;

    private ApiProcessHarness(Process process, HttpClient client, string baseUrl)
    {
        _process = process;
        Client = client;
        BaseUrl = baseUrl;
    }

    public HttpClient Client { get; }

    public string BaseUrl { get; }

    public const string TestToken = "specfleettesttoken";

    public const string TestUserId = "test-user";

    public static async Task<ApiProcessHarness> StartAsync(
        string connectionString,
        string? geminiApiKey = null,
        string? geminiModel = null,
        string? geminiBaseUrl = null,
        string? gitHubBaseUrl = null,
        string? gitHubAccessToken = null,
        IEnumerable<string>? gitHubAllowedWorkflows = null,
        string? openSpecWorkspaceRoot = null,
        string? uiOrigin = null)
    {
        var projectDirectory = LocateApiProjectDirectory();
        var assemblyPath = await BuildApiAsync(projectDirectory);
        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";

        var startInfo = new ProcessStartInfo("dotnet", $"\"{assemblyPath}\"")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__SpecFleet"] = connectionString;
        startInfo.Environment["SpecFleet__Auth__Tokens__" + TestToken] = TestUserId;
        if (geminiApiKey is not null)
        {
            startInfo.Environment["Gemini__ApiKey"] = geminiApiKey;
        }

        if (geminiModel is not null)
        {
            startInfo.Environment["Gemini__Model"] = geminiModel;
        }

        if (geminiBaseUrl is not null)
        {
            startInfo.Environment["Gemini__BaseUrl"] = geminiBaseUrl;
        }

        if (gitHubBaseUrl is not null)
        {
            startInfo.Environment["GitHub__BaseUrl"] = gitHubBaseUrl;
        }

        if (gitHubAccessToken is not null)
        {
            startInfo.Environment["GitHub__AccessToken"] = gitHubAccessToken;
        }

        if (gitHubAllowedWorkflows is not null)
        {
            var index = 0;
            foreach (var workflow in gitHubAllowedWorkflows)
            {
                startInfo.Environment[$"GitHub__AllowedWorkflows__{index++}"] = workflow;
            }
        }

        if (openSpecWorkspaceRoot is not null)
        {
            startInfo.Environment["OpenSpec__WorkspaceRoot"] = openSpecWorkspaceRoot;
        }

        if (uiOrigin is not null)
        {
            startInfo.Environment["SpecFleet__Ui__AllowedOrigins__0"] = uiOrigin;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the API process.");

        var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestToken);

        try
        {
            await WaitForHealthAsync(client);
        }
        catch
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }

            throw;
        }

        return new ApiProcessHarness(process, client, baseUrl);
    }

    private static async Task WaitForHealthAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException exception)
            {
                last = exception;
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"The API did not become healthy. Last error: {last?.Message}");
    }

    private static string LocateApiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpecFleet.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root.");
        }

        return Path.Combine(directory.FullName, "src", "SpecFleet.Api");
    }

    private static async Task<string> BuildApiAsync(string projectDirectory)
    {
        var assemblyPath = Path.Combine(
            projectDirectory, "bin", "Debug", "net10.0", "SpecFleet.Api.dll");
        if (File.Exists(assemblyPath))
        {
            return assemblyPath;
        }

        var buildInfo = new ProcessStartInfo("dotnet", "build SpecFleet.Api.csproj -nodeReuse:false")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var build = Process.Start(buildInfo)
            ?? throw new InvalidOperationException("Failed to start the API build.");

        var output = await build.StandardOutput.ReadToEndAsync();
        var error = await build.StandardError.ReadToEndAsync();
        await build.WaitForExitAsync();

        if (build.ExitCode != 0 || !File.Exists(assemblyPath))
        {
            throw new InvalidOperationException($"The API build failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }

        return assemblyPath;
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }

        _process.Dispose();
    }
}
