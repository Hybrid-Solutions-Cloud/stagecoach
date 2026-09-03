using Stagecoach.Core;

namespace Stagecoach.Tests;

/// <summary>
/// The updater decides whether to run an installer, so its version ordering and asset naming are
/// security-relevant, not cosmetic.
/// </summary>
public sealed class ReleaseUpdateTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("2.0.0", "10.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    public void VersionsOrderNumericallyNotLexically(string left, string right, int expected)
    {
        var first = Parse(left);
        var second = Parse(right);
        Assert.Equal(expected, Math.Sign(first.CompareTo(second)));
    }

    [Fact]
    public void PrereleaseSortsBeforeItsRelease()
    {
        Assert.True(Parse("1.0.0-beta.1").CompareTo(Parse("1.0.0")) < 0);
        Assert.True(Parse("1.0.0-beta.2").CompareTo(Parse("1.0.0-beta.10")) < 0);
        Assert.True(Parse("1.0.0-alpha").CompareTo(Parse("1.0.0-beta")) < 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("not-a-version")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-bad space")]
    public void MalformedVersionsAreRejected(string value) =>
        Assert.Null(GitHubReleaseUpdateService.ProductVersion.TryParse(value));

    [Fact]
    public void PackageNameMatchesTheReleasePipelineContract() =>
        Assert.Equal(
            "Stagecoach-1.2.3-win-x64.msi",
            GitHubReleaseUpdateService.BuildPackageName("1.2.3"));

    private static GitHubReleaseUpdateService.ProductVersion Parse(string value)
    {
        var parsed = GitHubReleaseUpdateService.ProductVersion.TryParse(value);
        Assert.NotNull(parsed);
        return parsed.Value;
    }
}
