using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Stagecoach.Infrastructure;

namespace Stagecoach.App.Security;

public enum AppOwnerKind
{
    /// <summary>No owner configured yet — first run has not completed.</summary>
    None,

    /// <summary>A Windows account. Verified with Windows Hello, or the Windows password Hello falls back to.</summary>
    WindowsAccount,

    /// <summary>A Microsoft Entra account, verified by signing in to it. Separate from connected identities.</summary>
    EntraAccount,
}

public sealed record AppOwnerRecord(
    int Version,
    AppOwnerKind Kind,
    string DisplayName,
    string? WindowsSid,
    string? EntraUserPrincipalName,
    string? PassphraseSalt,
    int PassphraseIterations,
    string? PassphraseVerifier)
{
    public bool HasPassphrase => PassphraseSalt is not null && PassphraseVerifier is not null;
}

/// <summary>
/// The account that owns this installation of Stagecoach, chosen once during first-run setup.
/// <para>
/// This is deliberately **not** one of the connected Entra identities. Those exist to discover and
/// reach machines; this one decides who may open the application and read its local estate. Vault
/// Prospector draws the same line, and conflating them was the mistake this replaces.
/// </para>
/// <para>
/// A passphrase is always set alongside the chosen method. Windows Hello and an Entra sign-in prove
/// *who is present*; neither yields key material. The passphrase is what actually protects the
/// database key, and it is also the fallback when Hello cannot prompt — inside a remote session,
/// for example, where it never can.
/// </para>
/// </summary>
public static class AppOwner
{
    private const int Version = 1;
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int DerivedBytes = 32;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ConfigPath => Path.Combine(StagecoachPaths.RootDirectory, "owner.json");

    public static AppOwnerRecord? Current => Read();

    public static bool IsConfigured => Current is { Kind: not AppOwnerKind.None };

    /// <summary>The Windows account running right now, as a display name and SID.</summary>
    public static (string Name, string Sid) CurrentWindowsAccount()
    {
        if (!OperatingSystem.IsWindows()) return (Environment.UserName, string.Empty);
        using var identity = WindowsIdentity.GetCurrent();
        return (identity.Name, identity.User?.Value ?? string.Empty);
    }

    /// <summary>
    /// Completes first-run setup. Returns the entropy the metadata store must be opened with, so the
    /// caller can re-wrap the database key under it.
    /// </summary>
    public static byte[] Configure(
        AppOwnerKind kind,
        string displayName,
        string passphrase,
        string? entraUserPrincipalName = null)
    {
        if (kind == AppOwnerKind.None) throw new InvalidOperationException("Choose how Stagecoach should be secured.");
        ValidatePassphrase(passphrase);
        if (kind == AppOwnerKind.EntraAccount && string.IsNullOrWhiteSpace(entraUserPrincipalName))
            throw new InvalidOperationException("Sign in to the Entra account that should own Stagecoach first.");

        Directory.CreateDirectory(StagecoachPaths.RootDirectory);
        StagecoachPaths.AssertWritable(StagecoachPaths.RootDirectory);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var sid = kind == AppOwnerKind.WindowsAccount ? CurrentWindowsAccount().Sid : null;

        var record = new AppOwnerRecord(
            Version,
            kind,
            string.IsNullOrWhiteSpace(displayName) ? "Stagecoach owner" : displayName.Trim(),
            sid,
            entraUserPrincipalName?.Trim(),
            Convert.ToBase64String(salt),
            Iterations,
            Convert.ToBase64String(Derive(passphrase, salt, "verifier", Iterations)));

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(record, Options));
        return Derive(passphrase, salt, "entropy", Iterations);
    }

    /// <summary>Checks the passphrase and returns the entropy for the metadata store, or null.</summary>
    public static byte[]? TryPassphrase(string passphrase)
    {
        if (Read() is not { } record || !record.HasPassphrase || string.IsNullOrEmpty(passphrase)) return null;
        var salt = Convert.FromBase64String(record.PassphraseSalt!);
        var candidate = Derive(passphrase, salt, "verifier", record.PassphraseIterations);
        var expected = Convert.FromBase64String(record.PassphraseVerifier!);
        return CryptographicOperations.FixedTimeEquals(candidate, expected)
            ? Derive(passphrase, salt, "entropy", record.PassphraseIterations)
            : null;
    }

    /// <summary>
    /// True when the Windows account at the keyboard is the one that owns this installation. Checked
    /// before Windows Hello, because Hello only proves the *current* user is present — it would
    /// happily verify a different Windows user who had signed in on this machine.
    /// </summary>
    public static bool CurrentWindowsAccountIsOwner() =>
        Read() is { Kind: AppOwnerKind.WindowsAccount, WindowsSid: { Length: > 0 } sid } &&
        string.Equals(sid, CurrentWindowsAccount().Sid, StringComparison.OrdinalIgnoreCase);

    public static bool EntraAccountIsOwner(string signedInUserPrincipalName) =>
        Read() is { Kind: AppOwnerKind.EntraAccount, EntraUserPrincipalName: { Length: > 0 } owner } &&
        string.Equals(owner, signedInUserPrincipalName.Trim(), StringComparison.OrdinalIgnoreCase);

    public static void Reset()
    {
        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
    }

    public static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 8)
            throw new InvalidOperationException("Choose a passphrase of at least 8 characters.");
        if (passphrase.Length > 256)
            throw new InvalidOperationException("That passphrase is longer than Stagecoach accepts.");
    }

    /// <summary>The isolated Azure CLI profile used only to verify the owning Entra account.</summary>
    public static string EntraOwnerConfigDirectory =>
        Path.Combine(StagecoachPaths.RootDirectory, "owner-azure");

    private static AppOwnerRecord? Read()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var text = File.ReadAllText(ConfigPath);
            return text.Length > 64 * 1024 ? null : JsonSerializer.Deserialize<AppOwnerRecord>(text, Options);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Two independent values from one passphrase. The verifier is stored; the entropy never is, and
    // the purpose-scoped salt keeps the stored verifier from revealing anything about it.
    private static byte[] Derive(string passphrase, byte[] salt, string purpose, int iterations)
    {
        var scoped = new byte[salt.Length + Encoding.UTF8.GetByteCount(purpose)];
        salt.CopyTo(scoped, 0);
        Encoding.UTF8.GetBytes(purpose, scoped.AsSpan(salt.Length));
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), scoped, iterations, HashAlgorithmName.SHA256, DerivedBytes);
    }
}
