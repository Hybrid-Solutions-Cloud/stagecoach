using System.Diagnostics;
using System.Text;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Azure;

public sealed class AzureCliRunner : IAzureCliRunner
{
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);

    public Task<CommandResult> RunAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(azureConfigDirectory, arguments, interactive: false, cancellationToken);

    public Task<CommandResult> RunInteractiveAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(azureConfigDirectory, arguments, interactive: true, cancellationToken);

    public Task<IManagedCommand> StartBackgroundAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(azureConfigDirectory);
        StagecoachPaths.EnsureDirectories();
        var startInfo = CreateStartInfo(azureConfigDirectory, arguments, createNoWindow: true);
        if (environment is not null)
            foreach (var item in environment) startInfo.Environment[item.Key] = item.Value;
        return Task.FromResult<IManagedCommand>(new ManagedCommand(startInfo));
    }

    private static async Task<CommandResult> RunCoreAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        bool interactive,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(azureConfigDirectory);
        Directory.CreateDirectory(azureConfigDirectory);
        StagecoachPaths.EnsureDirectories();

        var startInfo = CreateStartInfo(azureConfigDirectory, arguments, createNoWindow: !interactive);
        startInfo.RedirectStandardInput = !interactive;
        startInfo.RedirectStandardOutput = !interactive;
        startInfo.RedirectStandardError = !interactive;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Azure CLI could not be started.");

        var stdoutTask = interactive ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = interactive ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            using var grace = new CancellationTokenSource(ExitGrace);
            try { await process.WaitForExitAsync(grace.Token); } catch (OperationCanceledException) { }
            throw;
        }

        return new CommandResult(
            process.ExitCode,
            await stdoutTask,
            Redact(await stderrTask));
    }

    private static ProcessStartInfo CreateStartInfo(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        bool createNoWindow)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "az",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = createNoWindow,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["AZURE_CONFIG_DIR"] = azureConfigDirectory;
        startInfo.Environment["AZURE_EXTENSION_DIR"] = StagecoachPaths.ExtensionDirectory;
        startInfo.Environment["AZURE_CORE_COLLECT_TELEMETRY"] = "false";
        startInfo.Environment["AZURE_CORE_ONLY_SHOW_ERRORS"] = "true";
        startInfo.Environment["AZURE_CORE_NO_COLOR"] = "true";
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.Where(line =>
            !line.Contains("accessToken", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("Authorization:", StringComparison.OrdinalIgnoreCase)));
    }

    private static void TryKillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private sealed class ManagedCommand : IManagedCommand
    {
        private const int MaximumLines = 200;
        private readonly Process _process;
        private readonly Queue<string> _safeOutput = new();
        private readonly object _gate = new();

        public ManagedCommand(ProcessStartInfo startInfo)
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!_process.Start()) throw new InvalidOperationException("Azure CLI background helper could not be started.");
            _process.OutputDataReceived += (_, args) => Capture(args.Data);
            _process.ErrorDataReceived += (_, args) => Capture(args.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Completion = WaitAsync();
        }

        public int ProcessId => _process.Id;
        public Task<int> Completion { get; }

        public IReadOnlyList<string> GetSafeOutput()
        {
            lock (_gate) return _safeOutput.ToArray();
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_process.HasExited) return;
            TryKillTree(_process);
            await _process.WaitForExitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited) await StopAsync();
            _process.Dispose();
        }

        private async Task<int> WaitAsync()
        {
            await _process.WaitForExitAsync();
            return _process.ExitCode;
        }

        private void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var redacted = Redact(line);
            if (string.IsNullOrWhiteSpace(redacted)) return;
            lock (_gate)
            {
                while (_safeOutput.Count >= MaximumLines) _safeOutput.Dequeue();
                _safeOutput.Enqueue(redacted);
            }
        }
    }
}
