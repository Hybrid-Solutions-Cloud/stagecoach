using Microsoft.Data.Sqlite;
using Stagecoach.App.ViewModels;
using Stagecoach.Core;
using Stagecoach.Infrastructure;
using Stagecoach.Infrastructure.Storage;

namespace Stagecoach.Tests;

/// <summary>
/// An operator hit "read-only" when adding an account and the application had no way to say which
/// folder was at fault, because storage failures collapsed into "unexpected local error" and
/// write-ahead logging was assumed to work everywhere. These cover both.
/// </summary>
public sealed class LocalStateResilienceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "stagecoach-tests", Guid.NewGuid().ToString("N"));

    public LocalStateResilienceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void AssertWritable_AcceptsAnOrdinaryWritableDirectory() =>
        StagecoachPaths.AssertWritable(_directory);

    [Fact]
    public void AssertWritable_NamesTheDirectoryAndTheLikelyCauseWhenItCannotBeUsed()
    {
        // A file standing where the folder should be reproduces the same failure shape as a
        // blocked or redirected folder without needing to manipulate ACLs.
        var blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "not a directory");

        var exception = Assert.Throws<InvalidOperationException>(() => StagecoachPaths.AssertWritable(blocker));

        Assert.Contains(blocker, exception.Message, StringComparison.Ordinal);
        Assert.Contains("OneDrive", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("antivirus", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("elevated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task StoreInitializesAndStaysUsableWhateverJournalModeIsAvailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = Path.Combine(_directory, "state", "stagecoach.db");
        var store = new EncryptedSqliteMetadataStore(databasePath, Path.Combine(_directory, "state", "test.key"));
        await store.InitializeAsync(cancellationToken);

        // Initialization must leave a database that can actually be written to, not merely opened.
        var identity = new AzureIdentityProfile(
            Guid.NewGuid(), "Probe", "probe@example.invalid",
            Path.Combine(_directory, "azure"), AuthenticationState.Ready, DateTimeOffset.UtcNow);
        await store.UpsertIdentityAsync(identity, cancellationToken);
        Assert.Single(await store.GetIdentitiesAsync(cancellationToken));

        // Re-initialising an existing database must not regress either.
        await store.InitializeAsync(cancellationToken);
        Assert.Single(await store.GetIdentitiesAsync(cancellationToken));
    }

    [Fact]
    public void SafeMessage_ExplainsAReadOnlyDatabaseInsteadOfSayingUnexpectedError()
    {
        var message = MainViewModel.SafeMessage(
            new SqliteException("attempt to write a readonly database", 8));

        Assert.Contains("local database", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(StagecoachPaths.RootDirectory, message, StringComparison.Ordinal);
        Assert.Contains("OneDrive", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unexpected local error", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeMessage_KeepsTheTypeAndTextForAnythingUnrecognised()
    {
        var message = MainViewModel.SafeMessage(new FormatException("bad token shape"));

        // The old catch-all discarded both, which is what made the original report undiagnosable.
        Assert.Contains(nameof(FormatException), message, StringComparison.Ordinal);
        Assert.Contains("bad token shape", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashLogPathLivesUnderTheLocalStateFolder() =>
        Assert.StartsWith(StagecoachPaths.LogsDirectory, CrashLogPath, StringComparison.OrdinalIgnoreCase);

    private static string CrashLogPath =>
        (string)typeof(MainViewModel).Assembly
            .GetType("Stagecoach.App.CrashLog")!
            .GetProperty("LogPath")!
            .GetValue(null)!;
}
