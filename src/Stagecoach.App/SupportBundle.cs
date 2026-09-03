using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Stagecoach.Core;
using Stagecoach.Infrastructure;

namespace Stagecoach.App;

/// <summary>
/// Collects everything support needs to diagnose a failure into one zip the operator can attach to
/// a message. Deliberately narrow: the error log, an environment summary, and readiness results.
/// It never touches the metadata database, the isolated Azure CLI profiles, or Windows Credential
/// Manager, so no token, password, or Azure identifier can leave the machine in it.
/// </summary>
internal static class SupportBundle
{
    private const long MaximumLogBytes = 4 * 1024 * 1024;

    public static string Directory => Path.Combine(StagecoachPaths.RootDirectory, "support");

    public static async Task<string> CreateAsync(
        string applicationVersion,
        WorkstationReadiness? readiness,
        string? lastStatusMessage,
        CancellationToken cancellationToken = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        StagecoachPaths.AssertWritable(Directory);

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(Directory, $"stagecoach-support-{stamp}.zip");

        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(
                archive, "environment.txt",
                DescribeEnvironment(applicationVersion, readiness, lastStatusMessage),
                cancellationToken);

            // The error log is the point of the exercise; include it whole unless it is huge.
            if (File.Exists(CrashLog.LogPath))
            {
                var entry = archive.CreateEntry("stagecoach-errors.log", CompressionLevel.Optimal);
                await using var target = entry.Open();
                await using var source = new FileStream(
                    CrashLog.LogPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
                if (source.Length > MaximumLogBytes) source.Seek(-MaximumLogBytes, SeekOrigin.End);
                await source.CopyToAsync(target, cancellationToken);
            }
            else
            {
                await WriteEntryAsync(
                    archive, "stagecoach-errors.log",
                    "No errors have been recorded on this machine.", cancellationToken);
            }

            await WriteEntryAsync(archive, "local-state.txt", DescribeLocalState(), cancellationToken);
        }

        return path;
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive, string name, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await using var writer = new StreamWriter(target, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static string DescribeEnvironment(
        string applicationVersion, WorkstationReadiness? readiness, string? lastStatusMessage)
    {
        var builder = new StringBuilder()
            .AppendLine("Stagecoach support bundle")
            .AppendLine($"Collected            : {DateTimeOffset.Now:u}")
            .AppendLine($"Stagecoach version   : {applicationVersion}")
            .AppendLine($"Operating system     : {RuntimeInformation.OSDescription}")
            .AppendLine($"Architecture         : {RuntimeInformation.OSArchitecture}")
            .AppendLine($".NET runtime         : {RuntimeInformation.FrameworkDescription}")
            .AppendLine($"Remote session       : {SystemParametersRemoteSession()}")
            .AppendLine($"Interactive session  : {Environment.UserInteractive}")
            .AppendLine();

        if (lastStatusMessage is { Length: > 0 })
            builder.AppendLine($"Last status message  : {lastStatusMessage}").AppendLine();

        if (readiness is null)
        {
            builder.AppendLine("Workstation readiness: not yet inspected.");
        }
        else
        {
            builder
                .AppendLine("Workstation readiness")
                .AppendLine($"  Windows            : {readiness.IsWindows}")
                .AppendLine($"  Azure CLI          : {readiness.HasAzureCli} ({readiness.AzureCliVersion?.ToString() ?? "unknown"})")
                .AppendLine($"  Azure CLI path     : {TryResolveCli()}")
                .AppendLine($"  ssh extension      : {readiness.HasSshExtension} ({readiness.SshExtensionVersion?.ToString() ?? "none"})")
                .AppendLine($"  Bastion commands   : {readiness.HasBastionCommands}")
                .AppendLine($"  OpenSSH client     : {readiness.HasOpenSsh}")
                .AppendLine($"  Remote Desktop     : {readiness.HasMstsc}");
            foreach (var action in readiness.Actions) builder.AppendLine($"  Action             : {action}");
        }

        return builder.ToString();
    }

    private static string TryResolveCli()
    {
        try { return Infrastructure.Azure.AzureCliRunner.ResolveAzureCliPath(); }
        catch (Exception exception) { return $"not found — {exception.Message}"; }
    }

    private static string SystemParametersRemoteSession()
    {
        // Worth capturing: an interactive Microsoft sign-in behaves differently over a remote session.
        try { return Environment.GetEnvironmentVariable("SESSIONNAME") ?? "unknown"; }
        catch (Exception) { return "unknown"; }
    }

    /// <summary>
    /// File names, sizes, and timestamps only — never contents. The database and the Azure CLI
    /// profiles hold tokens and identifiers and must not be collected.
    /// </summary>
    private static string DescribeLocalState()
    {
        var builder = new StringBuilder()
            .AppendLine("Local state inventory (names and sizes only; no file contents)")
            .AppendLine($"Root: {StagecoachPaths.RootDirectory}")
            .AppendLine();
        try
        {
            var root = new DirectoryInfo(StagecoachPaths.RootDirectory);
            if (!root.Exists) return builder.AppendLine("The local state folder does not exist.").ToString();

            foreach (var item in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                         .OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                         .Take(400))
            {
                var relative = Path.GetRelativePath(root.FullName, item.FullName);
                builder.AppendLine(item is FileInfo file
                    ? $"  {relative,-70} {file.Length,12:N0}  {file.LastWriteTime:u}"
                    : $"  {relative,-70} {"<dir>",12}  {item.LastWriteTime:u}");
            }
        }
        catch (Exception exception)
        {
            builder.AppendLine($"Could not enumerate local state: {exception.Message}");
        }

        return builder.ToString();
    }
}
