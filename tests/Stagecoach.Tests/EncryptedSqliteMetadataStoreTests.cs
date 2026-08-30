using Stagecoach.Core;
using Stagecoach.Infrastructure.Storage;

namespace Stagecoach.Tests;

public sealed class EncryptedSqliteMetadataStoreTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "stagecoach-tests", Guid.NewGuid().ToString("N"));
    private EncryptedSqliteMetadataStore Store => new(Path.Combine(_directory, "test.db"), Path.Combine(_directory, "test.key"));

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_directory); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); return ValueTask.CompletedTask; }

    [Fact]
    public async Task IdentityScopeAndMachineRoundTrip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = Store;
        await store.InitializeAsync(cancellationToken);
        var identity = new AzureIdentityProfile(Guid.NewGuid(), "Lab", "operator@example.com", "C:\\isolated", AuthenticationState.Ready, DateTimeOffset.UtcNow);
        var tenant = new TenantScope(identity.Id, "tenant", "Tenant", true);
        var subscription = new SubscriptionScope(identity.Id, "tenant", "subscription", "Subscription", "Enabled", true);
        await store.UpsertIdentityInventoryAsync(new IdentityInventory(identity, [tenant], [subscription]), cancellationToken);
        var path = new AzureAccessPath(identity.Id, "tenant", "subscription", ConnectionRouteKind.BastionTunnelRdp, ReadinessState.Ready, "ready", "/bastion", true);
        var machine = new MachineRecord("/machine", "vm1", MachineKind.AzureVm, OperatingSystemKind.Windows, "Windows",
            "rg", "eastus", "running", "", "10.0.0.4", null, "/vnet", "corp.example.com",
            new Dictionary<string, string> { ["environment"] = "lab" }, [path], DateTimeOffset.UtcNow);
        await store.UpsertDiscoveryAsync(new DiscoveryResult(identity.Id, [machine], DateTimeOffset.UtcNow, []), cancellationToken);

        Assert.Single(await store.GetIdentitiesAsync(cancellationToken));
        Assert.True(Assert.Single(await store.GetTenantsAsync(identity.Id, cancellationToken)).IsEnabled);
        Assert.True(Assert.Single(await store.GetSubscriptionsAsync(identity.Id, cancellationToken)).IsEnabled);
        var loaded = Assert.Single(await store.GetMachinesAsync(cancellationToken));
        Assert.Equal("corp.example.com", loaded.DomainName);
        Assert.Equal(ConnectionRouteKind.BastionTunnelRdp, Assert.Single(loaded.AccessPaths).Route);
    }

    [Fact]
    public async Task DatabaseCannotBeReadWithoutDpapiProtectedKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = Store;
        await store.InitializeAsync(cancellationToken);
        Assert.True(File.Exists(Path.Combine(_directory, "test.key")));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_directory, "test.db"), cancellationToken);
        var header = System.Text.Encoding.ASCII.GetBytes("SQLite format 3");
        Assert.False(bytes.AsSpan().StartsWith(header));
    }

    [Fact]
    public async Task SuccessfulRescanRemovesStalePathsButPreservesOtherIdentityAccess()
    {
        var token = TestContext.Current.CancellationToken;
        var store = Store;
        await store.InitializeAsync(token);
        var first = new AzureIdentityProfile(Guid.NewGuid(), "First", "first@example.com", "C:\\first", AuthenticationState.Ready, DateTimeOffset.UtcNow);
        var second = new AzureIdentityProfile(Guid.NewGuid(), "Second", "second@example.com", "C:\\second", AuthenticationState.Ready, DateTimeOffset.UtcNow);
        await store.UpsertIdentityAsync(first, token);
        await store.UpsertIdentityAsync(second, token);
        var firstPath = new AzureAccessPath(first.Id, "tenant", "subscription", ConnectionRouteKind.DirectRdp, ReadinessState.Ready, "ready");
        var secondPath = firstPath with { IdentityId = second.Id, Route = ConnectionRouteKind.BastionTunnelRdp };
        var baseMachine = new MachineRecord("/machine", "vm", MachineKind.AzureVm, OperatingSystemKind.Windows, "Windows", "rg", "eastus",
            "running", string.Empty, "10.0.0.4", null, "/vnet", null, new Dictionary<string, string>(), [firstPath], DateTimeOffset.UtcNow);
        await store.UpsertDiscoveryAsync(new DiscoveryResult(first.Id, [baseMachine], DateTimeOffset.UtcNow, []), token);
        await store.UpsertDiscoveryAsync(new DiscoveryResult(second.Id, [baseMachine with { AccessPaths = [secondPath] }], DateTimeOffset.UtcNow, []), token);

        await store.UpsertDiscoveryAsync(new DiscoveryResult(first.Id, [], DateTimeOffset.UtcNow, []), token);
        var machine = Assert.Single(await store.GetMachinesAsync(token));
        Assert.Equal(second.Id, Assert.Single(machine.AccessPaths).IdentityId);

        await store.UpsertDiscoveryAsync(new DiscoveryResult(second.Id, [], DateTimeOffset.UtcNow, []), token);
        Assert.Empty(await store.GetMachinesAsync(token));
    }
}
