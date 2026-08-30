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
}
