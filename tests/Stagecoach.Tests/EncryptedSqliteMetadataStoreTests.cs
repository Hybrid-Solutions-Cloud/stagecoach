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

    /// <summary>
    /// Machine flags must survive a write and a reopen with each value landing in its own column.
    /// <para>
    /// 0.6.0 read and wrote the Machines table by position with <c>SELECT *</c> and a bare
    /// <c>INSERT ... VALUES</c>. A database created before <c>SupportsEntraLogin</c> existed has
    /// that column appended to the end by ALTER TABLE, so its physical order differs from a fresh
    /// one and every value after ordinal 14 lined up against the wrong field — reading threw
    /// "data is NULL at ordinal 16" and rescans wrote flags into the wrong columns. Columns are
    /// named on both sides now.
    /// </para>
    /// <para>
    /// This covers the round trip and the distinctness of the two flags; it does not build a
    /// pre-migration table, because the store owns the encryption key and the test cannot reach past
    /// it to reshape the schema.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MachineFlagsRoundTripIntoTheirOwnColumns()
    {
        var token = TestContext.Current.CancellationToken;
        var databasePath = Path.Combine(_directory, "test.db");
        var keyPath = Path.Combine(_directory, "test.key");

        var store = new EncryptedSqliteMetadataStore(databasePath, keyPath);
        await store.InitializeAsync(token);
        var identity = new AzureIdentityProfile(
            Guid.NewGuid(), "Lab", "operator@example.com", "C:\\isolated", AuthenticationState.Ready, DateTimeOffset.UtcNow);
        await store.UpsertIdentityAsync(identity, token);
        var path = new AzureAccessPath(identity.Id, "tenant", "subscription", ConnectionRouteKind.DirectRdp, ReadinessState.Ready, "ready");

        // Entra sign-in set, favourite not — so the two cannot be confused for one another.
        var machine = new MachineRecord(
            "/machine", "vm1", MachineKind.AzureVm, OperatingSystemKind.Windows, "Windows", "rg", "eastus",
            "running", string.Empty, "10.0.0.4", null, "/vnet", null, new Dictionary<string, string>(),
            [path], DateTimeOffset.UtcNow)
        { SupportsEntraLogin = true };
        await store.UpsertDiscoveryAsync(new DiscoveryResult(identity.Id, [machine], DateTimeOffset.UtcNow, []), token);

        // Reopening re-runs the migration step, which must not disturb what is already stored.
        var reopened = new EncryptedSqliteMetadataStore(databasePath, keyPath);
        await reopened.InitializeAsync(token);
        var loaded = Assert.Single(await reopened.GetMachinesAsync(token));
        Assert.Equal("vm1", loaded.Name);
        Assert.Equal("10.0.0.4", loaded.PrivateIpAddress);
        Assert.True(loaded.SupportsEntraLogin);
        Assert.False(loaded.IsFavorite);
        Assert.Null(loaded.LastConnectedAt);
        Assert.Equal(ConnectionRouteKind.DirectRdp, Assert.Single(loaded.AccessPaths).Route);
    }

    /// <summary>
    /// The one-time removal of the passphrase Stagecoach used to require. An existing installation
    /// has its key wrapped with entropy derived from that passphrase; removing it must unwrap once
    /// and rewrap under Windows protection alone, leaving the estate readable with nothing typed.
    /// Getting this wrong locks an operator out of their own data on update, so it is tested.
    /// </summary>
    [Fact]
    public async Task RemovingTheLegacyPassphraseLeavesTheEstateReadable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Metadata protection is Windows-only.");
        var token = TestContext.Current.CancellationToken;
        var identity = new AzureIdentityProfile(
            Guid.NewGuid(), "Lab", "operator@example.com", "C:\\isolated", AuthenticationState.Ready, DateTimeOffset.UtcNow);

        // An installation as it exists today: the key is wrapped with passphrase-derived entropy.
        var passphraseEntropy = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var original = Store;
        await original.InitializeAsync(token);
        await original.UpsertIdentityAsync(identity, token);
        original.RewrapKey(passphraseEntropy);

        // Without that entropy the key cannot be unwrapped at all — which is exactly why the
        // passphrase has to be given one final time rather than simply dropped.
        var withoutEntropy = Store;
        await Assert.ThrowsAnyAsync<Exception>(async () => await withoutEntropy.InitializeAsync(token));

        // The removal itself.
        var migrating = Store;
        migrating.UseAdditionalEntropy(passphraseEntropy);
        migrating.RewrapKey(null);

        // A later start, with nothing supplied, opens the same estate.
        var after = Store;
        await after.InitializeAsync(token);
        Assert.Equal(identity.Id, Assert.Single(await after.GetIdentitiesAsync(token)).Id);

        // And the interrupted case: the key was rewrapped but the owner record was not updated
        // before the process died. Rewrapping again with no entropy must succeed rather than throw,
        // which is what lets the next launch finish the removal without asking for anything.
        var interrupted = Store;
        interrupted.RewrapKey(null);
        var settled = Store;
        await settled.InitializeAsync(token);
        Assert.Equal(identity.Id, Assert.Single(await settled.GetIdentitiesAsync(token)).Id);
    }
}
