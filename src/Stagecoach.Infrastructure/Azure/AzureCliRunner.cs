using System.Diagnostics;
using System.Text;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Azure;

public sealed class AzureCliRunner : IAzureCliRunner
{
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long an interactive Microsoft sign-in may stay open before Stagecoach gives up. Long
    /// enough for Conditional Access, MFA, and a device-code round trip; short enough that a prompt
    /// which never appeared cannot wedge the application indefinitely.
    /// </summary>
    public static readonly TimeSpan InteractiveTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The ceiling for a command that needs no human. Generous, because installing extensions over a
    /// slow link is legitimately slow — but finite, because a command with no bound can hold the
    /// whole application busy indefinitely.
    /// </summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    public Task<CommandResult> RunAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(azureConfigDirectory, arguments, interactive: false, progress: null, cancellationToken);

    public Task<CommandResult> RunInteractiveAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(azureConfigDirectory, arguments, interactive: true, progress, cancellationToken);

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
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(azureConfigDirectory);
        Directory.CreateDirectory(azureConfigDirectory);
        StagecoachPaths.EnsureDirectories();

        // Output is captured for interactive commands too. It previously was not, which meant
        // every 'az login' failure arrived as an empty string and every one of them produced the
        // same unusable message. Standard input stays attached so a broker or browser flow is
        // unaffected; only the two output pipes are read.
        var startInfo = CreateStartInfo(azureConfigDirectory, arguments, createNoWindow: true);
        startInfo.RedirectStandardInput = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Azure CLI could not be started.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        // Read stderr line by line so device-code instructions reach the operator while the sign-in
        // is still open. Reading it only at exit would mean the code is shown after it is useless.
        var stderrBuffer = new StringBuilder();
        var stderrTask = PumpAsync(process.StandardError, stderrBuffer, progress, cancellationToken);

        // Every command is bounded. An interactive sign-in that is never completed used to hang the
        // caller forever; so did an ordinary command that stalls — "az extension add" waiting on a
        // network that never answers left one operator's application busy for eight hours, and while
        // it was busy every other action in the application silently did nothing.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(interactive ? InteractiveTimeout : CommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKillTree(process);
            await WaitForExitGraceAsync(process);
            var partial = Redact(stderrBuffer.ToString());
            if (!interactive)
            {
                return new CommandResult(
                    -1,
                    string.Empty,
                    string.IsNullOrWhiteSpace(partial)
                        ? $"The Azure CLI did not respond within {CommandTimeout.TotalMinutes:0} minutes and was stopped."
                        : partial);
            }

            return new CommandResult(
                -1,
                string.Empty,
                string.IsNullOrWhiteSpace(partial)
                    ? $"Sign-in did not complete within {InteractiveTimeout.TotalMinutes:0} minutes. " +
                      "No Microsoft sign-in prompt was completed. If no prompt appeared, use device-code sign-in."
                    : partial);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            await WaitForExitGraceAsync(process);
            throw;
        }

        return new CommandResult(
            process.ExitCode,
            await stdoutTask,
            Redact(await stderrTask));
    }

    private static async Task WaitForExitGraceAsync(Process process)
    {
        using var grace = new CancellationTokenSource(ExitGrace);
        try { await process.WaitForExitAsync(grace.Token); } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Drains a stream line by line, keeping the whole thing for the caller and forwarding
    /// operator-relevant lines (device codes, sign-in URLs) as they arrive.
    /// </summary>
    private static async Task<string> PumpAsync(
        StreamReader reader,
        StringBuilder buffer,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                buffer.AppendLine(line);
                if (progress is not null && IsOperatorRelevant(line)) progress.Report(line.Trim());
            }
        }
        catch (OperationCanceledException)
        {
            // Whatever arrived before cancellation is still worth reporting.
        }
        catch (IOException)
        {
            // The pipe closes when the process is killed; not an error worth surfacing.
        }

        return buffer.ToString();
    }

    private static string? _resolvedCliPath;

    /// <summary>
    /// Finds the Azure CLI launcher. On Windows the CLI is <c>az.cmd</c>, and starting a process
    /// with <c>UseShellExecute = false</c> goes through CreateProcess, which does no PATHEXT
    /// resolution — so a bare "az" fails with "the system cannot find the file specified" on a
    /// machine where the CLI is installed and working. The extensionless <c>az</c> shell script
    /// that ships alongside it is not runnable by CreateProcess either, so it is skipped.
    /// </summary>
    public static string ResolveAzureCliPath()
    {
        if (_resolvedCliPath is { } cached && File.Exists(cached)) return cached;

        foreach (var candidate in EnumerateCandidates())
        {
            if (!File.Exists(candidate)) continue;
            _resolvedCliPath = candidate;
            return candidate;
        }

        throw new InvalidOperationException(
            "Stagecoach could not find the Azure CLI (az.cmd) on this machine. Install it from " +
            "https://aka.ms/installazurecliwindows, then reopen Stagecoach. If it is already " +
            "installed, make sure its 'wbin' folder is on PATH for your user account.");
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        string[] names = ["az.cmd", "az.bat", "az.exe"];

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0) continue;
            foreach (var name in names)
            {
                string candidate;
                try { candidate = Path.Combine(trimmed, name); }
                catch (ArgumentException) { continue; }
                yield return candidate;
            }
        }

        // Default installer locations, for the common case where PATH was not refreshed after install.
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var name in names)
                yield return Path.Combine(root, "Microsoft SDKs", "Azure", "CLI2", "wbin", name);
        }
    }

    private static bool IsOperatorRelevant(string line) =>
        line.Contains("devicelogin", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("enter the code", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("To sign in", StringComparison.OrdinalIgnoreCase);

    private static ProcessStartInfo CreateStartInfo(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        bool createNoWindow)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveAzureCliPath(),
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

        // Set here as well as in each profile's configuration, because this is the one place every
        // invocation passes through. A profile that has not been configured yet -- or was created by
        // an older version -- still cannot stop to ask a question, and a question asked of a hidden
        // process with redirected input only ever ends in "EOF when reading a line".
        startInfo.Environment["AZURE_CORE_LOGIN_EXPERIENCE_V2"] = "off";
        startInfo.Environment["AZURE_EXTENSION_USE_DYNAMIC_INSTALL"] = "yes_without_prompt";
        startInfo.Environment["AZURE_EXTENSION_DYNAMIC_INSTALL_ALLOW_PREVIEW"] = "false";
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
