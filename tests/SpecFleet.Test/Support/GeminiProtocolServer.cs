using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SpecFleet.Test.Support;

/// <summary>
/// A real local HTTP server that speaks the Gemini streaming (SSE) protocol. It lets the
/// real <c>GeminiChatCompletionProvider</c> be exercised over real HTTP without a network
/// dependency on the hosted Gemini service, so adapter parsing, streaming, error, and
/// cancellation behavior are verified against the genuine adapter code path.
/// It is not a mock of Service logic.
/// </summary>
public sealed class GeminiProtocolServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Func<HttpListenerContext, Task> _handler;

    private GeminiProtocolServer(string prefix, Func<HttpListenerContext, Task> handler)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _handler = handler;
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public string BaseUrl { get; private init; } = string.Empty;

    public static GeminiProtocolServer Start(Func<HttpListenerContext, Task> handler)
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        return new GeminiProtocolServer(prefix, handler) { BaseUrl = $"http://127.0.0.1:{port}" };
    }

    public static GeminiProtocolServer StreamingText(params string[] chunks) =>
        Start(async context =>
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            await using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8);
            foreach (var chunk in chunks)
            {
                await writer.WriteLineAsync($"data: {{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":\"{chunk}\"}}]}}}}]}}");
                await writer.FlushAsync();
            }

            context.Response.Close();
        });

    public static GeminiProtocolServer Failing(int statusCode) =>
        Start(context =>
        {
            context.Response.StatusCode = statusCode;
            context.Response.Close();
            return Task.CompletedTask;
        });

    /// <summary>
    /// Streams chunks with a delay between them so a client can cancel mid-response.
    /// </summary>
    public static GeminiProtocolServer SlowStreamingText(params string[] chunks) =>
        Start(async context =>
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            await using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8);
            foreach (var chunk in chunks)
            {
                await writer.WriteLineAsync($"data: {{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":\"{chunk}\"}}]}}}}]}}");
                await writer.FlushAsync();
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400));
                }
                catch (Exception)
                {
                    return;
                }
            }

            context.Response.Close();
        });

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
