using System.Diagnostics;

namespace SpecFleet.Service.Features.OpenSpecOrchestration;

/// <summary>
/// Runs the OpenSpec CLI as a real child process. The argument vector is passed directly to
/// the executable with no shell, so arbitrary shell metacharacters cannot be injected.
/// </summary>
public sealed class ProcessOpenSpecCommandRunner(string executable = "openspec") : IOpenSpecCommandRunner
{
    public async Task<OpenSpecCommandResult> RunAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new OpenSpecCommandResult(-1, string.Empty, "The OpenSpec CLI could not be started.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new OpenSpecCommandResult(
                process.ExitCode,
                await standardOutput,
                await standardError);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The CLI is not installed or is not executable. Report it as an unsuccessful
            // operation rather than a transport failure so callers can surface the cause.
            return new OpenSpecCommandResult(
                -1,
                string.Empty,
                $"The OpenSpec CLI could not be executed: {exception.Message}");
        }
    }
}
