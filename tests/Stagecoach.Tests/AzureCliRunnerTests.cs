using Stagecoach.Infrastructure.Azure;

namespace Stagecoach.Tests;

/// <summary>
/// Stagecoach could not start the Azure CLI at all: the runner used a bare "az", and starting a
/// process with UseShellExecute=false goes through CreateProcess, which performs no PATHEXT
/// resolution. On Windows the CLI is az.cmd, so every Azure operation failed with "the system
/// cannot find the file specified" on machines where the CLI was installed and working.
/// </summary>
public sealed class AzureCliRunnerTests
{
    [Fact]
    public void ResolvesAnExecutableAzureCliLauncher()
    {
        var path = AzureCliRunner.ResolveAzureCliPath();

        Assert.True(File.Exists(path), $"Resolved Azure CLI path does not exist: {path}");

        // CreateProcess cannot run the extensionless shell script that ships beside az.cmd, so the
        // resolver must never select it.
        var extension = Path.GetExtension(path);
        Assert.False(string.IsNullOrEmpty(extension), $"Resolved a launcher with no extension: {path}");
        Assert.Contains(extension, new[] { ".cmd", ".bat", ".exe" }, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolutionIsStableAcrossCalls() =>
        Assert.Equal(AzureCliRunner.ResolveAzureCliPath(), AzureCliRunner.ResolveAzureCliPath());

    [Fact]
    public async Task RunAsyncCapturesOutputFromTheRealAzureCli()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runner = new AzureCliRunner();
        var directory = Path.Combine(Path.GetTempPath(), "stagecoach-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = await runner.RunAsync(directory, ["version", "--output", "json"], cancellationToken);

            Assert.True(result.Succeeded, $"az version failed: {result.StandardError}");
            Assert.Contains("azure-cli", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void InteractiveSignInIsBounded()
    {
        // An unfinished sign-in previously waited forever, because the only cancellation source
        // was a token the UI never supplied.
        Assert.InRange(
            AzureCliRunner.InteractiveTimeout,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(15));
    }
}
