namespace Stagecoach.Infrastructure;

public static class StagecoachPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Stagecoach");

    public static string DatabasePath => Path.Combine(RootDirectory, "stagecoach.db");
    public static string DatabaseKeyPath => Path.Combine(RootDirectory, "stagecoach.db.key");
    public static string IdentityDirectory => Path.Combine(RootDirectory, "identities");
    public static string ExtensionDirectory => Path.Combine(RootDirectory, "azure-cli-extensions");
    public static string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public static string IdentityConfigDirectory(Guid identityId) =>
        Path.Combine(IdentityDirectory, identityId.ToString("D"), "azure");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(IdentityDirectory);
        Directory.CreateDirectory(ExtensionDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// Confirms Stagecoach can actually create, write, and delete a file in <paramref name="directory"/>.
    /// Every store, profile, and credential operation depends on this, and when it is not true the
    /// underlying components report it in ways that mean nothing to an operator — SQLite in
    /// particular says "attempt to write a readonly database". Failing here names the directory and
    /// the likely cause instead.
    /// </summary>
    public static void AssertWritable(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var probe = Path.Combine(directory, $".stagecoach-write-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
            }

            File.Delete(probe);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Stagecoach cannot write to its local state folder '{directory}'. " +
                "This usually means the folder is redirected to OneDrive or a network share, " +
                "controlled-folder-access or antivirus is blocking it, or the folder was created by " +
                "a different (elevated) account. Check that folder, then reopen Stagecoach. " +
                $"Underlying error: {exception.Message}",
                exception);
        }
    }
}
