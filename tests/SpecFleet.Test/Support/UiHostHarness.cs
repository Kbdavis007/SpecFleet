using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SpecFleet.Test.Support;

/// <summary>
/// Serves the independently built React/TypeScript UI from its own origin, separate from the
/// API process, so Playwright can verify the deployed dashboard against the real API over
/// HTTP. The UI is built with its own toolchain (npm/vite) and never served by the API.
/// </summary>
public sealed class UiHostHarness : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly string _distRoot;

    private UiHostHarness(HttpListener listener, string distRoot, string baseUrl)
    {
        _listener = listener;
        _distRoot = distRoot;
        BaseUrl = baseUrl;
        _loop = Task.Run(AcceptLoopAsync);
    }

    public string BaseUrl { get; }

    public static async Task<UiHostHarness> StartAsync()
    {
        var distRoot = await EnsureBuiltAsync();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        return new UiHostHarness(listener, distRoot, $"http://127.0.0.1:{port}");
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

            _ = Task.Run(() => RespondAsync(context));
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path is "/" or "")
            {
                path = "/index.html";
            }

            var relative = path.TrimStart('/');
            var candidate = Path.GetFullPath(Path.Combine(_distRoot, relative));
            if (!candidate.StartsWith(_distRoot, StringComparison.Ordinal) || !File.Exists(candidate))
            {
                // Single-page application fallback.
                candidate = Path.Combine(_distRoot, "index.html");
            }

            var bytes = await File.ReadAllBytesAsync(candidate);
            context.Response.StatusCode = 200;
            context.Response.ContentType = ContentTypeFor(candidate);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception)
        {
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };

    private static async Task<string> EnsureBuiltAsync()
    {
        var uiDirectory = LocateUiDirectory();
        var distRoot = Path.Combine(uiDirectory, "dist");
        var index = Path.Combine(distRoot, "index.html");
        if (File.Exists(index))
        {
            return distRoot;
        }

        if (!Directory.Exists(Path.Combine(uiDirectory, "node_modules")))
        {
            await RunNpmAsync(uiDirectory, "install");
        }

        await RunNpmAsync(uiDirectory, "run build");
        if (!File.Exists(index))
        {
            throw new InvalidOperationException("The UI build did not produce dist/index.html.");
        }

        return distRoot;
    }

    private static async Task RunNpmAsync(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("npm", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to run 'npm {arguments}'.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The UI command 'npm {arguments}' failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private static string LocateUiDirectory()
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

        return Path.Combine(directory.FullName, "src", "SpecFleet.Ui");
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
