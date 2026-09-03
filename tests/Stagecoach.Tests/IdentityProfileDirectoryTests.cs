using Stagecoach.Core;
using Stagecoach.Infrastructure;

namespace Stagecoach.Tests;

/// <summary>
/// Adding an account failed every time with "a file or directory with the same name already
/// exists": the destination folder was created and then immediately moved onto. These cover the
/// directory shape the promotion relies on, and the move itself.
/// </summary>
public sealed class IdentityProfileDirectoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "stagecoach-tests", Guid.NewGuid().ToString("N"));

    public IdentityProfileDirectoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void IdentityConfigDirectoryIsTheAzureFolderBeneathAnIdFolder()
    {
        var id = Guid.NewGuid();
        var path = StagecoachPaths.IdentityConfigDirectory(id);

        Assert.Equal("azure", Path.GetFileName(path));
        Assert.Equal(id.ToString("D"), Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal(
            StagecoachPaths.IdentityDirectory,
            Path.GetDirectoryName(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void CreatingTheDestinationBeforeMovingOntoItAlwaysFails()
    {
        // This is precisely what the identity service used to do, and why sign-in could never
        // complete. Pinned so the shape cannot quietly come back.
        var source = Path.Combine(_root, "pending", "azure");
        var destination = Path.Combine(_root, "final", "azure");
        Directory.CreateDirectory(source);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        Assert.Throws<IOException>(() =>
            Directory.Move(Path.GetDirectoryName(source)!, Path.GetDirectoryName(destination)!));
    }

    [Fact]
    public void CreatingOnlyTheParentLetsThePromotionSucceed()
    {
        var source = Path.Combine(_root, "identities", "pending", "azure");
        var destination = Path.Combine(_root, "identities", "final", "azure");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "msal.cache"), "profile state");

        var sourceRoot = Path.GetDirectoryName(source)!;
        var destinationRoot = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationRoot)!);
        Directory.Move(sourceRoot, destinationRoot);

        Assert.True(File.Exists(Path.Combine(destination, "msal.cache")));
        Assert.False(Directory.Exists(sourceRoot));
    }

    [Fact]
    public void AnOrphanedDestinationFromAFailedAttemptCanBeClearedAndReplaced()
    {
        var source = Path.Combine(_root, "identities", "pending", "azure");
        var destinationRoot = Path.Combine(_root, "identities", "final");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "msal.cache"), "new profile");
        Directory.CreateDirectory(Path.Combine(destinationRoot, "azure"));
        File.WriteAllText(Path.Combine(destinationRoot, "azure", "stale.txt"), "leftover");

        Directory.Delete(destinationRoot, recursive: true);
        Directory.Move(Path.GetDirectoryName(source)!, destinationRoot);

        Assert.True(File.Exists(Path.Combine(destinationRoot, "azure", "msal.cache")));
        Assert.False(File.Exists(Path.Combine(destinationRoot, "azure", "stale.txt")));
    }
}
