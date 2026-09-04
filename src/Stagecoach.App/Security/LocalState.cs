using Stagecoach.Infrastructure;

namespace Stagecoach.App.Security;

/// <summary>
/// Removing everything Stagecoach keeps on this machine, so setup can run again.
/// <para>
/// This is the escape hatch behind "Start fresh": the way back in when the owning account can no
/// longer be verified — a rebuilt Windows profile, an Entra account that no longer exists. Nothing
/// in Azure is touched; it is the local cache, the owner record, and the signed-in profiles.
/// </para>
/// </summary>
public static class LocalState
{
    public static void StartFresh()
    {
        AppOwner.Reset();

        DeleteFile(StagecoachPaths.DatabasePath);
        DeleteFile(StagecoachPaths.DatabasePath + "-wal");
        DeleteFile(StagecoachPaths.DatabasePath + "-shm");
        DeleteFile(StagecoachPaths.DatabaseKeyPath);
        DeleteFile(Path.Combine(StagecoachPaths.RootDirectory, "lock.json"));

        DeleteDirectory(StagecoachPaths.IdentityDirectory);
        DeleteDirectory(AppOwner.EntraOwnerConfigDirectory);
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file still held open must not stop the rest being removed; the caller reports it.
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
