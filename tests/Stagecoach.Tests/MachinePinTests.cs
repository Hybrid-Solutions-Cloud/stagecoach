using Stagecoach.Core;
using Stagecoach.Infrastructure.Storage;

namespace Stagecoach.Tests;

/// <summary>
/// A pinned local account is what makes a machine connect on the first click instead of asking.
/// These cover the persistence side of that promise.
/// </summary>
public sealed class MachinePinTests : IAsyncLifetime
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "stagecoach-tests", Guid.NewGuid().ToString("N"));

    private EncryptedSqliteMetadataStore Store =>
        new(Path.Combine(_directory, "test.db"), Path.Combine(_directory, "test.key"));

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PinRoundTripsAndIsCaseInsensitiveOnResourceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = Store;
        await store.InitializeAsync(cancellationToken);
        var account = new ConnectionIdentityProfile(
            Guid.NewGuid(), "Prod local admin", ConnectionIdentityKind.LocalAccount, "svcadmin", null, null);
        await store.UpsertConnectionIdentityAsync(account, cancellationToken);

        await store.SetMachinePinAsync("/subscriptions/a/machines/VM1", account.Id, cancellationToken);

        var pins = await store.GetMachinePinsAsync(cancellationToken);
        Assert.Equal(account.Id, pins["/SUBSCRIPTIONS/A/MACHINES/VM1"]);
        Assert.Equal(account.Id, pins["/subscriptions/a/machines/vm1"]);
    }

    [Fact]
    public async Task PinCanBeRepointedAndCleared()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = Store;
        await store.InitializeAsync(cancellationToken);
        var first = new ConnectionIdentityProfile(
            Guid.NewGuid(), "First", ConnectionIdentityKind.LocalAccount, "one", null, null);
        var second = new ConnectionIdentityProfile(
            Guid.NewGuid(), "Second", ConnectionIdentityKind.ActiveDirectory, "CORP\\two", null, null);
        await store.UpsertConnectionIdentityAsync(first, cancellationToken);
        await store.UpsertConnectionIdentityAsync(second, cancellationToken);

        await store.SetMachinePinAsync("/machine", first.Id, cancellationToken);
        await store.SetMachinePinAsync("/machine", second.Id, cancellationToken);
        Assert.Equal(second.Id, (await store.GetMachinePinsAsync(cancellationToken))["/MACHINE"]);

        await store.SetMachinePinAsync("/machine", null, cancellationToken);
        Assert.Empty(await store.GetMachinePinsAsync(cancellationToken));
    }

    [Fact]
    public async Task RemovingAnAccountDropsItsPins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = Store;
        await store.InitializeAsync(cancellationToken);
        var account = new ConnectionIdentityProfile(
            Guid.NewGuid(), "Temp", ConnectionIdentityKind.LocalAccount, "temp", null, null);
        await store.UpsertConnectionIdentityAsync(account, cancellationToken);
        await store.SetMachinePinAsync("/machine", account.Id, cancellationToken);

        await store.RemoveConnectionIdentityAsync(account.Id, cancellationToken);

        // The machine falls back to asking rather than pointing at a credential that is gone.
        Assert.Empty(await store.GetMachinePinsAsync(cancellationToken));
    }
}
