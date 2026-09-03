using System.Diagnostics;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Updates;

/// <summary>
/// Hands a verified Stagecoach MSI to Windows Installer. The elevation prompt belongs to Windows,
/// not Stagecoach, so the operator always sees what is being installed.
/// </summary>
public sealed class WindowsUpdateInstallerLauncher : IUpdateInstallerLauncher
{
    public void Launch(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        var resolvedPath = Path.GetFullPath(installerPath);
        if (!string.Equals(Path.GetExtension(resolvedPath), ".msi", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(resolvedPath))
            throw new InvalidDataException("The verified Windows installer is unavailable.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add("/i");
        startInfo.ArgumentList.Add(resolvedPath);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Windows Installer did not start.");
    }
}
