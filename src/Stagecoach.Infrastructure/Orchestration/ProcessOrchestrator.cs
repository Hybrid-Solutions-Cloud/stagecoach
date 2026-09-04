using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Stagecoach.Core;
using Stagecoach.Infrastructure.Security;

namespace Stagecoach.Infrastructure.Orchestration;

public sealed class ProcessOrchestrator(
    IAzureCliRunner cli,
    IConnectionCredentialStore credentialStore,
    IMetadataStore metadataStore) : IConnectionService
{
    private readonly ConcurrentDictionary<Guid, SessionRuntime> _sessions = new();

    public async Task<ConnectionSession> ConnectAsync(
        MachineRecord machine,
        AzureAccessPath accessPath,
        AzureIdentityProfile azureIdentity,
        ConnectionIdentityProfile? targetIdentity,
        ConnectionIdentityProfile? relayIdentity,
        CancellationToken cancellationToken = default)
    {
        if (accessPath.IdentityId != azureIdentity.Id)
            throw new InvalidOperationException("The selected Azure identity does not own this access path.");
        if (accessPath.Readiness is ReadinessState.MissingPrerequisite or ReadinessState.Offline or ReadinessState.PermissionDenied or ReadinessState.Unsupported)
            throw new InvalidOperationException(accessPath.Reason);

        var session = new ConnectionSession(
            Guid.NewGuid(), machine.ResourceId, machine.Name, accessPath.Route, azureIdentity.Id,
            targetIdentity?.Id, DateTimeOffset.UtcNow, SessionState.Starting, SafeStatus: "Preparing connection");
        var runtime = new SessionRuntime(session);
        if (!_sessions.TryAdd(session.Id, runtime)) throw new InvalidOperationException("A session ID collision occurred.");

        try
        {
            switch (accessPath.Route)
            {
                case ConnectionRouteKind.DirectRdp:
                    await StartDirectRdpAsync(runtime, machine, targetIdentity, cancellationToken);
                    break;
                case ConnectionRouteKind.BastionTunnelRdp:
                    await StartBastionTunnelRdpAsync(runtime, machine, accessPath, azureIdentity, targetIdentity, cancellationToken);
                    break;
                case ConnectionRouteKind.BastionRdp:
                    await StartBastionNativeRdpAsync(runtime, machine, accessPath, azureIdentity, cancellationToken);
                    break;
                case ConnectionRouteKind.ArcRdp:
                    await StartArcRdpAsync(runtime, machine, accessPath, azureIdentity, targetIdentity, relayIdentity, cancellationToken);
                    break;
                case ConnectionRouteKind.DirectSsh:
                case ConnectionRouteKind.BastionSsh:
                case ConnectionRouteKind.ArcSsh:
                    await StartSshAsync(runtime, machine, accessPath, azureIdentity, targetIdentity, cancellationToken);
                    break;
                default:
                    throw new NotSupportedException($"Connection route {accessPath.Route} is not supported.");
            }
            await metadataStore.RecordConnectionAsync(machine.ResourceId, cancellationToken);
            return runtime.Session;
        }
        catch
        {
            runtime.Session = runtime.Session with { State = SessionState.Failed, SafeStatus = "Connection launch failed" };
            await runtime.DisposeAsync();
            throw;
        }
    }

    public Task<IReadOnlyList<ConnectionSession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ConnectionSession>>(_sessions.Values
            .Select(item => item.Session).OrderByDescending(item => item.StartedAt).ToArray());
    }

    public async Task StopAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryRemove(sessionId, out var runtime)) return;
        runtime.Session = runtime.Session with { State = SessionState.Stopping, SafeStatus = "Stopping" };
        await runtime.StopAsync(cancellationToken);
        runtime.Session = runtime.Session with { State = SessionState.Stopped, SafeStatus = "Stopped" };
        await runtime.DisposeAsync();
    }

    private async Task StartDirectRdpAsync(SessionRuntime runtime, MachineRecord machine, ConnectionIdentityProfile? targetIdentity, CancellationToken cancellationToken)
    {
        var endpoint = machine.PublicIpAddress ?? machine.PrivateIpAddress
            ?? throw new InvalidOperationException("The selected machine has no direct address.");
        runtime.CredentialLease = await StageCredentialAsync(endpoint, targetIdentity, cancellationToken);
        runtime.Client = StartMstsc(endpoint, targetIdentity?.Username);
        runtime.Session = runtime.Session with { State = SessionState.Active, ClientProcessId = runtime.Client.Id, SafeStatus = $"RDP {endpoint}" };
        _ = WatchClientAsync(runtime);
    }

    private async Task StartBastionTunnelRdpAsync(
        SessionRuntime runtime,
        MachineRecord machine,
        AzureAccessPath accessPath,
        AzureIdentityProfile identity,
        ConnectionIdentityProfile? targetIdentity,
        CancellationToken cancellationToken)
    {
        var (resourceGroup, name) = ParseAzureResource(accessPath.BastionResourceId, "bastionHosts");
        var port = ReservePort();
        runtime.Helper = await cli.StartBackgroundAsync(identity.AzureConfigDirectory,
            ["network", "bastion", "tunnel", "--name", name, "--resource-group", resourceGroup,
             "--subscription", accessPath.SubscriptionId, "--target-resource-id", machine.ResourceId,
             "--resource-port", "3389", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            cancellationToken: cancellationToken);
        await WaitForPortAsync(port, runtime.Helper, cancellationToken);
        var endpoint = $"localhost:{port}";
        runtime.CredentialLease = await StageCredentialAsync(endpoint, targetIdentity, cancellationToken);
        runtime.Client = StartMstsc(endpoint, targetIdentity?.Username);
        runtime.Session = runtime.Session with
        {
            State = SessionState.Active,
            HelperProcessId = runtime.Helper.ProcessId,
            ClientProcessId = runtime.Client.Id,
            LocalPort = port,
            SafeStatus = $"Bastion tunnel on localhost:{port}",
        };
        _ = WatchClientAsync(runtime);
    }

    private async Task StartBastionNativeRdpAsync(
        SessionRuntime runtime,
        MachineRecord machine,
        AzureAccessPath accessPath,
        AzureIdentityProfile identity,
        CancellationToken cancellationToken)
    {
        var (resourceGroup, name) = ParseAzureResource(accessPath.BastionResourceId, "bastionHosts");
        runtime.Helper = await cli.StartBackgroundAsync(identity.AzureConfigDirectory,
            ["network", "bastion", "rdp", "--name", name, "--resource-group", resourceGroup,
             "--subscription", accessPath.SubscriptionId, "--target-resource-id", machine.ResourceId, "--enable-mfa"],
            cancellationToken: cancellationToken);

        // Nothing here opens a window that Stagecoach can watch for, so the only evidence the
        // connection began is that the helper is still running. Reporting a session the instant the
        // process was started meant a command that failed immediately -- an Azure CLI without
        // Bastion support, for one -- was reported as connected while nothing ever appeared.
        await EnsureHelperSurvivedAsync(runtime.Helper, "Bastion RDP", cancellationToken);

        runtime.Session = runtime.Session with
        {
            State = SessionState.InteractionRequired,
            HelperProcessId = runtime.Helper.ProcessId,
            SafeStatus = "Bastion native RDP may require Microsoft Entra authentication",
        };
        _ = WatchHelperAsync(runtime);
    }

    private async Task StartArcRdpAsync(
        SessionRuntime runtime,
        MachineRecord machine,
        AzureAccessPath accessPath,
        AzureIdentityProfile identity,
        ConnectionIdentityProfile? targetIdentity,
        ConnectionIdentityProfile? relayIdentity,
        CancellationToken cancellationToken)
    {
        relayIdentity ??= targetIdentity;
        if (relayIdentity is null || string.IsNullOrWhiteSpace(relayIdentity.Username))
            throw new InvalidOperationException("Arc RDP requires a mapped SSH relay identity.");
        var environment = await BuildAskPassEnvironmentAsync(relayIdentity, cancellationToken);
        runtime.CredentialLease = await StageCredentialAsync("localhost", targetIdentity, cancellationToken);
        var arguments = new List<string>
        {
            "ssh", "arc", "--subscription", accessPath.SubscriptionId, "--resource-group", machine.ResourceGroup,
            "--name", machine.Name, "--local-user", relayIdentity.Username, "--rdp",
        };
        if (!string.IsNullOrWhiteSpace(relayIdentity.SshPrivateKeyPath))
        {
            arguments.Add("--private-key-file");
            arguments.Add(relayIdentity.SshPrivateKeyPath);
        }
        runtime.Helper = await cli.StartBackgroundAsync(identity.AzureConfigDirectory, arguments, environment, cancellationToken);
        runtime.Session = runtime.Session with
        {
            State = accessPath.Readiness == ReadinessState.InteractionRequired ? SessionState.InteractionRequired : SessionState.Active,
            HelperProcessId = runtime.Helper.ProcessId,
            SafeStatus = "Arc RDP relay starting",
        };
        _ = WatchHelperAsync(runtime);
    }

    private async Task StartSshAsync(
        SessionRuntime runtime,
        MachineRecord machine,
        AzureAccessPath path,
        AzureIdentityProfile identity,
        ConnectionIdentityProfile? connectionIdentity,
        CancellationToken cancellationToken)
    {
        var terminal = FindWindowsTerminal();
        var startInfo = new ProcessStartInfo { FileName = terminal, UseShellExecute = false };
        startInfo.Environment["AZURE_CONFIG_DIR"] = identity.AzureConfigDirectory;
        startInfo.Environment["AZURE_EXTENSION_DIR"] = StagecoachPaths.ExtensionDirectory;
        if (Path.GetFileName(terminal).Equals("wt.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("--window");
            startInfo.ArgumentList.Add("new");
        }
        else
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
        }
        if (path.Route == ConnectionRouteKind.DirectSsh)
        {
            startInfo.ArgumentList.Add("ssh.exe");
            if (!string.IsNullOrWhiteSpace(connectionIdentity?.SshPrivateKeyPath))
            {
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(connectionIdentity.SshPrivateKeyPath);
            }
            var endpoint = machine.PublicIpAddress ?? machine.PrivateIpAddress ?? throw new InvalidOperationException("No direct SSH address is available.");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(connectionIdentity?.Username) ? endpoint : $"{connectionIdentity.Username}@{endpoint}");
        }
        else
        {
            startInfo.ArgumentList.Add("az");
            if (path.Route == ConnectionRouteKind.ArcSsh)
            {
                startInfo.ArgumentList.Add("ssh"); startInfo.ArgumentList.Add("arc");
                startInfo.ArgumentList.Add("--subscription"); startInfo.ArgumentList.Add(path.SubscriptionId);
                startInfo.ArgumentList.Add("--resource-group"); startInfo.ArgumentList.Add(machine.ResourceGroup);
                startInfo.ArgumentList.Add("--name"); startInfo.ArgumentList.Add(machine.Name);
                if (!string.IsNullOrWhiteSpace(connectionIdentity?.Username))
                {
                    startInfo.ArgumentList.Add("--local-user"); startInfo.ArgumentList.Add(connectionIdentity.Username);
                }
            }
            else
            {
                var (resourceGroup, name) = ParseAzureResource(path.BastionResourceId, "bastionHosts");
                startInfo.ArgumentList.Add("network"); startInfo.ArgumentList.Add("bastion"); startInfo.ArgumentList.Add("ssh");
                startInfo.ArgumentList.Add("--name"); startInfo.ArgumentList.Add(name);
                startInfo.ArgumentList.Add("--resource-group"); startInfo.ArgumentList.Add(resourceGroup);
                startInfo.ArgumentList.Add("--subscription"); startInfo.ArgumentList.Add(path.SubscriptionId);
                startInfo.ArgumentList.Add("--target-resource-id"); startInfo.ArgumentList.Add(machine.ResourceId);
                startInfo.ArgumentList.Add("--auth-type"); startInfo.ArgumentList.Add("AAD");
            }
        }
        runtime.Client = Process.Start(startInfo) ?? throw new InvalidOperationException("SSH terminal could not be started.");
        runtime.Session = runtime.Session with { State = SessionState.Active, ClientProcessId = runtime.Client.Id, SafeStatus = "SSH terminal opened" };
        _ = WatchClientAsync(runtime);
        await Task.CompletedTask;
    }

    private async Task<TemporaryCredentialLease?> StageCredentialAsync(string endpoint, ConnectionIdentityProfile? profile, CancellationToken cancellationToken)
    {
        if (profile is null || profile.Kind is ConnectionIdentityKind.MicrosoftEntra or ConnectionIdentityKind.SshKey or ConnectionIdentityKind.PromptOnly) return null;
        var credential = await credentialStore.ReadAsync(profile.Id, cancellationToken)
            ?? throw new InvalidOperationException($"The Windows credential for '{profile.DisplayName}' is missing.");
        if (credentialStore is not WindowsCredentialManager windows)
            throw new InvalidOperationException("The configured credential store cannot stage Remote Desktop credentials.");
        return windows.StageRemoteDesktop(endpoint, credential.Username, credential.Password);
    }

    private async Task<IReadOnlyDictionary<string, string>?> BuildAskPassEnvironmentAsync(ConnectionIdentityProfile profile, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(profile.SshPrivateKeyPath)) return null;
        if (await credentialStore.ReadAsync(profile.Id, cancellationToken) is null) return null;
        var askPass = Path.Combine(AppContext.BaseDirectory, "Stagecoach.AskPass.exe");
        if (!File.Exists(askPass)) throw new InvalidOperationException("The Stagecoach SSH AskPass helper is missing.");
        return new Dictionary<string, string>
        {
            ["SSH_ASKPASS"] = askPass,
            ["SSH_ASKPASS_REQUIRE"] = "force",
            ["DISPLAY"] = "stagecoach",
            ["STAGECOACH_ASKPASS_PROFILE"] = profile.Id.ToString("D"),
        };
    }

    private async Task WatchClientAsync(SessionRuntime runtime)
    {
        try
        {
            if (runtime.Client is not null) await runtime.Client.WaitForExitAsync();
        }
        finally
        {
            await StopAsync(runtime.Session.Id);
        }
    }

    private async Task WatchHelperAsync(SessionRuntime runtime)
    {
        if (runtime.Helper is null) return;
        var exitCode = await runtime.Helper.Completion;
        if (_sessions.TryGetValue(runtime.Session.Id, out _))
            runtime.Session = runtime.Session with
            {
                State = exitCode == 0 ? SessionState.Stopped : SessionState.Failed,
                SafeStatus = exitCode == 0 ? "Session ended" : "Connection helper exited",
            };
    }

    private static Process StartMstsc(string endpoint, string? username)
    {
        var directory = Path.Combine(StagecoachPaths.RootDirectory, "sessions");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.rdp");
        var lines = new List<string>
        {
            $"full address:s:{endpoint}",
            "prompt for credentials:i:0",
            "authentication level:i:2",
            "enablecredsspsupport:i:1",
            "redirectclipboard:i:1",
            "screen mode id:i:2",
        };
        if (!string.IsNullOrWhiteSpace(username)) lines.Add($"username:s:{username}");
        File.WriteAllLines(path, lines);
        var info = new ProcessStartInfo { FileName = "mstsc.exe", UseShellExecute = false };
        info.ArgumentList.Add(path);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Remote Desktop Connection could not be started.");
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => TryDelete(path);
        return process;
    }

    private static int ReservePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Gives a helper a moment to fall over, and reports what it said if it does.
    /// <para>
    /// Used where there is no port or window to wait for. A missing Azure CLI Bastion extension, a
    /// bad resource, or an expired sign-in all end the process within a second or two, and without
    /// this the session was announced as connected regardless.
    /// </para>
    /// </summary>
    private static async Task EnsureHelperSurvivedAsync(
        IManagedCommand helper, string what, CancellationToken cancellationToken)
    {
        var settled = await Task.WhenAny(helper.Completion, Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));
        if (settled != helper.Completion) return;

        var detail = helper.GetSafeOutput().LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
        throw new InvalidOperationException(
            $"{what} ended immediately without connecting." +
            (detail is null
                ? " The Azure CLI reported nothing. Check that Bastion command support is installed under Settings."
                : $" The Azure CLI reported: {detail}"));
    }

    private static async Task WaitForPortAsync(int port, IManagedCommand helper, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        while (true)
        {
            if (helper.Completion.IsCompleted)
                throw new InvalidOperationException("The Bastion tunnel exited before its local endpoint became ready.");
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", port, timeout.Token);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(250, timeout.Token);
            }
        }
    }

    private static (string ResourceGroup, string Name) ParseAzureResource(string? resourceId, string expectedType)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) throw new InvalidOperationException("The access path is missing its Azure resource.");
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rgIndex = Array.FindIndex(parts, part => part.Equals("resourceGroups", StringComparison.OrdinalIgnoreCase));
        var typeIndex = Array.FindIndex(parts, part => part.Equals(expectedType, StringComparison.OrdinalIgnoreCase));
        if (rgIndex < 0 || rgIndex + 1 >= parts.Length || typeIndex < 0 || typeIndex + 1 >= parts.Length)
            throw new InvalidOperationException("The access path contains an invalid Azure resource identifier.");
        return (parts[rgIndex + 1], parts[typeIndex + 1]);
    }

    private static string FindWindowsTerminal()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(alias) ? alias : "cmd.exe";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class SessionRuntime(ConnectionSession session) : IAsyncDisposable
    {
        public ConnectionSession Session { get; set; } = session;
        public IManagedCommand? Helper { get; set; }
        public Process? Client { get; set; }
        public TemporaryCredentialLease? CredentialLease { get; set; }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Client is { HasExited: false })
            {
                try { Client.Kill(entireProcessTree: true); await Client.WaitForExitAsync(cancellationToken); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (Helper is not null) await Helper.StopAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            CredentialLease?.Dispose();
            Client?.Dispose();
            if (Helper is not null) await Helper.DisposeAsync();
        }
    }
}
