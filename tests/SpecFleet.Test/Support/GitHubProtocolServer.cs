using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SpecFleet.Test.Support;

/// <summary>
/// A real local HTTP server that speaks the subset of the GitHub REST API used by the
/// Service GitHub client. It lets the real client be exercised over real HTTP so
/// authorization, confirmation, path policy, dispatch, and failure handling are verified
/// against the genuine transport code path. It is not a mock of Service logic.
/// </summary>
public sealed class GitHubProtocolServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Func<HttpListenerContext, Task> _handler;

    private GitHubProtocolServer(string prefix, Func<HttpListenerContext, Task> handler)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _handler = handler;
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public string BaseUrl { get; private init; } = string.Empty;

    public static GitHubProtocolServer Start(Func<HttpListenerContext, Task> handler)
    {
        var port = GetFreePort();
        return new GitHubProtocolServer($"http://127.0.0.1:{port}/", handler)
        {
            BaseUrl = $"http://127.0.0.1:{port}"
        };
    }

    /// <summary>
    /// A server that grants or denies write permission and reports successful mutations.
    /// Denied requests return 403 so the client surfaces an authorization failure. The
    /// reference endpoints validate the request the way GitHub does: the base branch is
    /// resolved to a commit SHA and <c>POST /git/refs</c> rejects anything that is not a
    /// 40-character commit, so a client that sends a branch name is caught here.
    /// </summary>
    public static GitHubProtocolServer Authorizing(bool canWrite)
    {
        var createdRefs = new HashSet<string>(StringComparer.Ordinal);

        return Start(async context =>
        {
            if (!canWrite)
            {
                await WriteJsonAsync(context, 403, new { message = "Resource not accessible." });
                return;
            }

            await HandleSuccessAsync(context, createdRefs);
        });
    }

    /// <summary>
    /// A server that grants write permission but rejects every mutation with 422, exercising
    /// the "GitHub rejects a mutation" path where the failure must surface rather than be
    /// reported as a success-shaped outcome.
    /// </summary>
    public static GitHubProtocolServer RejectingMutation() =>
        Start(async context =>
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;

            if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                if (path.Contains("/git/ref/", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, 200, new { @object = new { sha = ValidSha } });
                    return;
                }

                await WriteJsonAsync(context, 200, new { permissions = new { push = true } });
                return;
            }

            await WriteJsonAsync(context, 422, new { message = "Validation failed." });
        });

    /// <summary>
    /// A server that grants write permission but rejects review-branch creation with the
    /// invalid-request 422 GitHub returns for a malformed SHA, exercising the path where a
    /// genuine 422 must surface as a failure rather than being tolerated as "already exists".
    /// </summary>
    public static GitHubProtocolServer RejectingReviewBranch() =>
        Start(async context =>
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;

            if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                if (path.Contains("/git/ref/", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, 200, new { @object = new { sha = ValidSha } });
                    return;
                }

                await WriteJsonAsync(context, 200, new { permissions = new { push = true } });
                return;
            }

            if (path.Contains("/git/refs", StringComparison.Ordinal))
            {
                await WriteJsonAsync(context, 422, new { message = "Invalid request. At least 40 characters are required." });
                return;
            }

            await WriteJsonAsync(context, 200, new { ok = true });
        });

    private const string ValidSha = "0123456789abcdef0123456789abcdef01234567";

    private static async Task HandleSuccessAsync(HttpListenerContext context, HashSet<string> createdRefs)
    {
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Contains("/git/ref/", StringComparison.Ordinal))
            {
                await WriteJsonAsync(context, 200, new { @object = new { sha = ValidSha } });
                return;
            }

            await WriteJsonAsync(context, 200, new { permissions = new { push = true } });
            return;
        }

        if (path.EndsWith("/issues", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, 201, new
            {
                number = 1,
                html_url = "https://github.com/example/repo/issues/1"
            });
            return;
        }

        if (path.EndsWith("/pulls", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, 201, new
            {
                number = 2,
                html_url = "https://github.com/example/repo/pull/2"
            });
            return;
        }

        if (path.Contains("/actions/workflows/", StringComparison.Ordinal) &&
            path.EndsWith("/dispatches", StringComparison.Ordinal))
        {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        if (path.EndsWith("/git/refs", StringComparison.Ordinal))
        {
            var body = await ReadJsonAsync(context);
            var reference = body.TryGetProperty("ref", out var refElement) ? refElement.GetString() : null;
            var sha = body.TryGetProperty("sha", out var shaElement) ? shaElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(sha) || sha.Length is not (40 or 64) ||
                !sha.All(Uri.IsHexDigit))
            {
                await WriteJsonAsync(context, 422, new
                {
                    message = "Invalid request. At least 40 characters are required; " +
                        $"{sha?.Length ?? 0} were supplied."
                });
                return;
            }

            if (reference is not null && !createdRefs.Add(reference))
            {
                await WriteJsonAsync(context, 422, new { message = "Reference already exists" });
                return;
            }

            await WriteJsonAsync(context, 201, new { @ref = reference });
            return;
        }

        if (path.Contains("/contents/", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, 201, new { content = new { sha = "abc123" } });
            return;
        }

        await WriteJsonAsync(context, 200, new { ok = true });
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(payload)
            ? default
            : JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object body)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(body);
        var bytes = Encoding.UTF8.GetBytes(payload);
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_cts.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }

            _ = Task.Run(() => _handler(context));
        }
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        _listener.Close();
        try
        {
            await _loop;
        }
        catch (Exception)
        {
            // Listener shutdown ends the accept loop.
        }

        _cts.Dispose();
    }
}
