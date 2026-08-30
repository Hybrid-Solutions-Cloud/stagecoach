using System.Diagnostics;
using System.Text.Json;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Readiness;

public sealed class WorkstationReadinessService(IAzureCliRunner cli) : IWorkstationReadinessService
{
    private static string BootstrapConfig => Path.Combine(StagecoachPaths.RootDirectory, "bootstrap-azure");

    public async Task<WorkstationReadiness> InspectAsync(CancellationToken cancellationToken = default)
    {
        var actions = new List<string>();
        var hasAzureCli = FindExecutable("az.exe") is not null || FindExecutable("az.cmd") is not null;
        Version? cliVersion = null;
        var hasSshExtension = false;
        Version? sshVersion = null;
        var hasBastion = false;
        if (hasAzureCli)
        {
            var version = await cli.RunAsync(BootstrapConfig, ["version", "--output", "json"], cancellationToken);
            if (version.Succeeded)
            {
                using var document = JsonDocument.Parse(version.StandardOutput);
                if (document.RootElement.TryGetProperty("azure-cli", out var value)) Version.TryParse(value.GetString(), out cliVersion);
            }
            var ssh = await cli.RunAsync(BootstrapConfig, ["extension", "show", "--name", "ssh", "--output", "json"], cancellationToken);
            hasSshExtension = ssh.Succeeded;
            if (ssh.Succeeded)
            {
                using var document = JsonDocument.Parse(ssh.StandardOutput);
                if (document.RootElement.TryGetProperty("version", out var value)) Version.TryParse(value.GetString(), out sshVersion);
                hasSshExtension = sshVersion is not null && sshVersion >= new Version(2, 0, 4);
            }
            var bastion = await cli.RunAsync(BootstrapConfig, ["network", "bastion", "--help"], cancellationToken);
            hasBastion = bastion.Succeeded;
        }

        var hasSsh = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe")) || FindExecutable("ssh.exe") is not null;
        var hasMstsc = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe"));
        if (!hasAzureCli) actions.Add("Install the current 64-bit Azure CLI for Windows.");
        else if (cliVersion is not null && cliVersion < new Version(2, 61, 0)) actions.Add("Update Azure CLI to 2.61.0 or later for Windows Web Account Manager sign-in.");
        if (!hasSshExtension) actions.Add("Install or update the Azure CLI ssh extension (2.0.4 or later).");
        if (!hasBastion) actions.Add("Install or update Azure CLI Bastion command support.");
        if (!hasSsh) actions.Add("Install the Windows OpenSSH Client optional capability.");
        if (!hasMstsc) actions.Add("Remote Desktop Connection (mstsc.exe) is unavailable.");

        return new WorkstationReadiness(
            OperatingSystem.IsWindows(), hasAzureCli, cliVersion, hasSshExtension, sshVersion,
            hasBastion, hasSsh, hasMstsc, actions);
    }

    public async Task PrepareCliExtensionsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var extension in new[] { "resource-graph", "ssh", "connectedmachine" })
        {
            var result = await cli.RunAsync(BootstrapConfig,
                ["extension", "add", "--upgrade", "--name", extension, "--yes"], cancellationToken);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Azure CLI extension '{extension}' could not be installed.");
        }
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => Path.Combine(item.Trim(), name))
            .FirstOrDefault(File.Exists);
    }
}
