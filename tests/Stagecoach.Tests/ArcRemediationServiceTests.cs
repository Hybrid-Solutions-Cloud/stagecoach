using Stagecoach.Core;
using Stagecoach.Infrastructure.Remediation;

namespace Stagecoach.Tests;

public sealed class ArcRemediationServiceTests
{
    [Fact]
    public void Preview_IsExplicitAndAzureWrite()
    {
        var cli = new RecordingCli();
        var service = new ArcRemediationService(cli);
        var (machine, path, _) = Context();
        var preview = service.PreviewOpenSshInstallation(machine, path);
        Assert.True(preview.RequiresAzureWrite);
        Assert.Contains("WindowsOpenSSH", preview.SafeOperations.Single());
        Assert.Empty(cli.Calls);
    }

    [Fact]
    public async Task Apply_UsesOwningIdentityAndExpectedPublisher()
    {
        var cli = new RecordingCli();
        var service = new ArcRemediationService(cli);
        var (machine, path, identity) = Context();
        var preview = service.PreviewOpenSshInstallation(machine, path);
        await service.ApplyOpenSshInstallationAsync(preview, machine, path, identity, TestContext.Current.CancellationToken);
        var call = Assert.Single(cli.Calls);
        Assert.Equal(identity.AzureConfigDirectory, call.Config);
        Assert.Contains("Microsoft.Azure.OpenSSH", call.Args);
        Assert.Contains(path.SubscriptionId, call.Args);
    }

    private static (MachineRecord Machine, AzureAccessPath Path, AzureIdentityProfile Identity) Context()
    {
        var identityId = Guid.NewGuid();
        var path = new AzureAccessPath(identityId, "tenant", "subscription", ConnectionRouteKind.ArcRdp,
            ReadinessState.MissingPrerequisite, "OpenSSH missing", IsPreferred: true);
        var machine = new MachineRecord("/subscriptions/subscription/resourceGroups/rg/providers/Microsoft.HybridCompute/machines/server1",
            "server1", MachineKind.ArcServer, OperatingSystemKind.Windows, "Windows Server 2025", "rg", "eastus",
            "Running", "Connected", null, null, null, "corp.example.com", new Dictionary<string, string>(), [path], DateTimeOffset.UtcNow);
        var identity = new AzureIdentityProfile(identityId, "Admin", "admin@example.com", "C:\\profiles\\admin", AuthenticationState.Ready, DateTimeOffset.UtcNow);
        return (machine, path, identity);
    }

    private sealed class RecordingCli : IAzureCliRunner
    {
        public List<(string Config, IReadOnlyList<string> Args)> Calls { get; } = [];
        public Task<CommandResult> RunAsync(string azureConfigDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add((azureConfigDirectory, arguments));
            return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
        }
        public Task<CommandResult> RunInteractiveAsync(string azureConfigDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IManagedCommand> StartBackgroundAsync(string azureConfigDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
