using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stagecoach.Infrastructure;

namespace Stagecoach.App;

public enum AppLockMode
{
    /// <summary>No unlock. Access to Stagecoach is whoever is at the Windows session.</summary>
    None,

    /// <summary>A passphrase is required, and it also protects the database key.</summary>
    Passphrase,
}

/// <summary>
/// An unlock step in front of Stagecoach, in the spirit of Vault Prospector's secure unlock.
/// <para>
/// The passphrase is never stored. It derives two independent values with PBKDF2: a verifier, kept
/// on disk purely to tell a wrong passphrase from a right one, and extra entropy mixed into the
/// Windows DPAPI protection of the database key. That second part is what makes this more than a
/// screen: without the passphrase the metadata key cannot be unwrapped at all, so simply being at
/// an unlocked Windows session is no longer enough to read the estate.
/// </para>
/// </summary>
internal static class AppLock
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int DerivedBytes = 32;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record LockFile(int Version, AppLockMode Mode, string Salt, int Iterations, string Verifier);

    public static string ConfigPath => Path.Combine(StagecoachPaths.RootDirectory, "lock.json");

    public static AppLockMode CurrentMode => Read()?.Mode ?? AppLockMode.None;

    public static bool IsEnabled => CurrentMode != AppLockMode.None;

    /// <summary>Turns the lock on. Returns the entropy the metadata store must be opened with.</summary>
    public static byte[] Enable(string passphrase)
    {
        ValidatePassphrase(passphrase);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        Directory.CreateDirectory(StagecoachPaths.RootDirectory);
        StagecoachPaths.AssertWritable(StagecoachPaths.RootDirectory);

        var file = new LockFile(
            1,
            AppLockMode.Passphrase,
            Convert.ToBase64String(salt),
            Iterations,
            Convert.ToBase64String(Derive(passphrase, salt, "verifier")));
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(file, Options));
        return Derive(passphrase, salt, "entropy");
    }

    public static void Disable()
    {
        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
    }

    /// <summary>
    /// Checks a passphrase and returns the entropy for the metadata store, or null when wrong.
    /// </summary>
    public static byte[]? TryUnlock(string passphrase)
    {
        if (Read() is not { Mode: AppLockMode.Passphrase } file) return null;
        if (string.IsNullOrEmpty(passphrase)) return null;

        var salt = Convert.FromBase64String(file.Salt);
        var candidate = Derive(passphrase, salt, "verifier", file.Iterations);
        var expected = Convert.FromBase64String(file.Verifier);

        // Fixed-time comparison so a wrong passphrase cannot be narrowed down by timing.
        return CryptographicOperations.FixedTimeEquals(candidate, expected)
            ? Derive(passphrase, salt, "entropy", file.Iterations)
            : null;
    }

    public static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 8)
            throw new InvalidOperationException("Choose a passphrase of at least 8 characters.");
        if (passphrase.Length > 256)
            throw new InvalidOperationException("That passphrase is longer than Stagecoach accepts.");
    }

    private static LockFile? Read()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var text = File.ReadAllText(ConfigPath);
            return text.Length > 64 * 1024 ? null : JsonSerializer.Deserialize<LockFile>(text, Options);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Two independent derivations from one passphrase: the purpose string keeps the verifier stored
    // on disk from revealing anything about the entropy that protects the key.
    private static byte[] Derive(string passphrase, byte[] salt, string purpose, int iterations = Iterations)
    {
        var scoped = new byte[salt.Length + Encoding.UTF8.GetByteCount(purpose)];
        salt.CopyTo(scoped, 0);
        Encoding.UTF8.GetBytes(purpose, scoped.AsSpan(salt.Length));
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), scoped, iterations, HashAlgorithmName.SHA256, DerivedBytes);
    }
}
