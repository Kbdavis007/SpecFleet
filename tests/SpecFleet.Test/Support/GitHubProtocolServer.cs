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
    /// Denied requests return 403 so the client surfaces an authorization failure.
    /// </summary>
    public static GitHubProtocolServer Authorizing(bool canWrite) =>
        Start(async context =>
        {
            if (!canWrite)
            {
                await WriteJsonAsync(context, 403, new { message = "Resource not accessible." });
                return;
            }

            await HandleSuccessAsync(context);
        });

    /// <summary>
    /// A server that grants write permission but rejects the mutation, exercising the
    /// "GitHub rejects a mutation" path.
    /// </summary>
    public static GitHubProtocolServer RejectingMutation() =>
        Start(async context =>
        {
            if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context, 200, new { permissions = new { push = true } });
                return;
            }

            await WriteJsonAsync(context, 422, new { message = "Validation failed." });
        });

    private static async Task HandleSuccessAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
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

        if (path.Contains("/contents/", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, 201, new { content = new { sha = "abc123" } });
            return;
        }

        if (path.Contains("/git/refs", StringComparison.Ordinal) ||
            path.Contains("/git/ref/", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, 201, new { @ref = "refs/heads/review" });
            return;
        }

        await WriteJsonAsync(context, 200, new { ok = true });
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
