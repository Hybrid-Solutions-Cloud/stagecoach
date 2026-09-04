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

    /// <summary>A Windows account. Verified with Windows Hello, or a Windows credential prompt.</summary>
    WindowsAccount,

    /// <summary>A Microsoft Entra account, verified by signing in to it. Separate from connected identities.</summary>
    EntraAccount,
}

/// <summary>
/// The owner record on disk. The three passphrase members are version 1 only — Stagecoach no longer
/// has a passphrase, and they are read solely so an existing installation can have its key unwrapped
/// once and rewrapped without one.
/// </summary>
public sealed record AppOwnerRecord(
    int Version,
    AppOwnerKind Kind,
    string DisplayName,
    string? WindowsSid,
    string? EntraUserPrincipalName,
    string? PassphraseSalt = null,
    int PassphraseIterations = 0,
    string? PassphraseVerifier = null)
{
    public bool HasLegacyPassphrase => PassphraseSalt is not null && PassphraseVerifier is not null;
}

/// <summary>
/// The account that owns this installation of Stagecoach, chosen once during first-run setup.
/// <para>
/// This is deliberately <b>not</b> one of the connected Entra identities. Those exist to discover and
/// reach machines; this one decides who may open the application and read its local estate.
/// </para>
/// <para>
/// There is <b>no application passphrase</b>, and there must never be one. Vault Prospector protects
/// its database with Windows data protection bound to the Windows account, and gates opening it with
/// a presence check — Windows Hello, falling back to a Windows credential prompt where Hello cannot
/// prompt. Stagecoach does exactly the same. Nothing the operator has to invent and remember.
/// </para>
/// </summary>
public static class AppOwner
{
    private const int Version = 2;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ConfigPath => Path.Combine(StagecoachPaths.RootDirectory, "owner.json");

    public static AppOwnerRecord? Current => Read();

    public static bool IsConfigured => Current is { Kind: not AppOwnerKind.None };

    /// <summary>True when this installation still has a passphrase from the version that had one.</summary>
    public static bool NeedsPassphraseRemoval => Current is { HasLegacyPassphrase: true };

    /// <summary>The Windows account running right now, as a display name and SID.</summary>
    public static (string Name, string Sid) CurrentWindowsAccount()
    {
        if (!OperatingSystem.IsWindows()) return (Environment.UserName, string.Empty);
        using var identity = WindowsIdentity.GetCurrent();
        return (identity.Name, identity.User?.Value ?? string.Empty);
    }

    /// <summary>
    /// The user principal name of the Windows account signed in right now, or null when it has none.
    /// <para>
    /// On a Microsoft Entra joined machine the Windows account <b>is</b> the Entra account, so this
    /// is what makes signing in again unnecessary: Windows already authenticated this person against
    /// Entra to create the session. Asking them to run an interactive sign-in on top of that proves
    /// nothing new.
    /// </para>
    /// </summary>
    public static string? CurrentWindowsUserPrincipalName()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var size = 0;
            GetUserNameEx(NameUserPrincipal, null, ref size);
            if (size <= 1) return null;

            var builder = new System.Text.StringBuilder(size);
            return GetUserNameEx(NameUserPrincipal, builder, ref size) && builder.Length > 0
                ? builder.ToString()
                : null;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    // EXTENDED_NAME_FORMAT.NameUserPrincipal
    private const int NameUserPrincipal = 8;

    [System.Runtime.InteropServices.DllImport("secur32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetUserNameEx(int format, System.Text.StringBuilder? name, ref int size);

    /// <summary>Completes first-run setup. Nothing is derived and nothing is typed.</summary>
    public static void Configure(AppOwnerKind kind, string displayName, string? entraUserPrincipalName = null)
    {
        if (kind == AppOwnerKind.None) throw new InvalidOperationException("Choose how Stagecoach should be secured.");
        if (kind == AppOwnerKind.EntraAccount && string.IsNullOrWhiteSpace(entraUserPrincipalName))
            throw new InvalidOperationException("Sign in to the Entra account that should own Stagecoach first.");

        Directory.CreateDirectory(StagecoachPaths.RootDirectory);
        StagecoachPaths.AssertWritable(StagecoachPaths.RootDirectory);

        Write(new AppOwnerRecord(
            Version,
            kind,
            string.IsNullOrWhiteSpace(displayName) ? "Stagecoach owner" : displayName.Trim(),
            kind == AppOwnerKind.WindowsAccount ? CurrentWindowsAccount().Sid : null,
            entraUserPrincipalName?.Trim()));
    }

    /// <summary>
    /// Checks a version 1 passphrase and returns the entropy its key was wrapped with, so the caller
    /// can rewrap without it. Returns null when the passphrase is wrong or none was ever set.
    /// </summary>
    public static byte[]? TryLegacyPassphrase(string passphrase)
    {
        if (Read() is not { HasLegacyPassphrase: true } record || string.IsNullOrEmpty(passphrase)) return null;
        var salt = Convert.FromBase64String(record.PassphraseSalt!);
        var candidate = Derive(passphrase, salt, "verifier", record.PassphraseIterations);
        var expected = Convert.FromBase64String(record.PassphraseVerifier!);
        return CryptographicOperations.FixedTimeEquals(candidate, expected)
            ? Derive(passphrase, salt, "entropy", record.PassphraseIterations)
            : null;
    }

    /// <summary>Drops the passphrase from the record. Call only after the key has been rewrapped without it.</summary>
    public static void CompletePassphraseRemoval()
    {
        if (Read() is not { } record) return;
        Write(record with
        {
            Version = Version,
            PassphraseSalt = null,
            PassphraseIterations = 0,
            PassphraseVerifier = null,
        });
    }

    /// <summary>
    /// True when the Windows account at the keyboard is the one that owns this installation. Checked
    /// before any prompt, because a presence check only proves the <i>current</i> user is there — it
    /// would happily verify a different Windows user who had signed in on this machine.
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

    /// <summary>The isolated Azure CLI profile used only to verify the owning Entra account.</summary>
    public static string EntraOwnerConfigDirectory =>
        Path.Combine(StagecoachPaths.RootDirectory, "owner-azure");

    private static void Write(AppOwnerRecord record) =>
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(record, Options));

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

    // Version 1 derivation, kept only to unwrap an existing key once. The purpose-scoped salt is why
    // the stored verifier revealed nothing about the entropy that protected the key.
    private static byte[] Derive(string passphrase, byte[] salt, string purpose, int iterations)
    {
        var scoped = new byte[salt.Length + Encoding.UTF8.GetByteCount(purpose)];
        salt.CopyTo(scoped, 0);
        Encoding.UTF8.GetBytes(purpose, scoped.AsSpan(salt.Length));
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), scoped, iterations, HashAlgorithmName.SHA256, 32);
    }
}
