using System.Text.Json;
using System.Text.Json.Serialization;
using Stagecoach.Core;
using Stagecoach.Infrastructure;

namespace Stagecoach.App;

/// <summary>
/// Moves an operator's setup to another machine: local account definitions without their passwords,
/// which account is pinned to which machine, and application preferences.
/// <para>
/// Deliberately excludes every secret. The SQLCipher key, Windows Credential Manager entries, and
/// the Azure CLI token caches are bound to one Windows account on one machine by design; copying
/// them would either fail or weaken that. After importing, the operator re-enters passwords once
/// and signs the Entra accounts in again.
/// </para>
/// </summary>
internal static class PortableSettings
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal sealed record LocalAccountExport(string Id, string DisplayName, int Kind, string Username, string? SshPrivateKeyPath);
    internal sealed record MachinePinExport(string ResourceId, string LocalAccountId);

    internal sealed record Payload(
        int Version,
        DateTimeOffset ExportedAt,
        string ExportedFrom,
        AppSettings Settings,
        IReadOnlyList<LocalAccountExport> LocalAccounts,
        IReadOnlyList<MachinePinExport> MachinePins);

    public static async Task<string> ExportAsync(
        IMetadataStore store,
        AppSettings settings,
        string path,
        CancellationToken cancellationToken = default)
    {
        var accounts = (await store.GetConnectionIdentitiesAsync(cancellationToken))
            .Select(item => new LocalAccountExport(
                item.Id.ToString("D"), item.DisplayName, (int)item.Kind, item.Username, item.SshPrivateKeyPath))
            .ToArray();

        var pins = (await store.GetMachinePinsAsync(cancellationToken))
            .Select(pair => new MachinePinExport(pair.Key, pair.Value.ToString("D")))
            .ToArray();

        var payload = new Payload(
            CurrentVersion,
            DateTimeOffset.Now,
            Environment.MachineName,
            settings,
            accounts,
            pins);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, payload, Options, cancellationToken);
        return path;
    }

    public static async Task<(int Accounts, int Pins)> ImportAsync(
        IMetadataStore store,
        AppSettingsStore settingsStore,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"No settings file was found at '{path}'.");

        await using var stream = File.OpenRead(path);
        if (stream.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("That settings file is larger than Stagecoach will read.");

        var payload = await JsonSerializer.DeserializeAsync<Payload>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("That file is not a Stagecoach settings export.");
        if (payload.Version is < 1 or > CurrentVersion)
            throw new InvalidDataException(
                $"That export is version {payload.Version}; this build reads up to {CurrentVersion}.");

        var accounts = 0;
        foreach (var account in payload.LocalAccounts ?? [])
        {
            if (!Guid.TryParse(account.Id, out var id) || string.IsNullOrWhiteSpace(account.Username)) continue;
            // CredentialTarget stays null: the password is not in the export and must be re-entered.
            await store.UpsertConnectionIdentityAsync(
                new ConnectionIdentityProfile(
                    id,
                    string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
                    Enum.IsDefined((ConnectionIdentityKind)account.Kind)
                        ? (ConnectionIdentityKind)account.Kind
                        : ConnectionIdentityKind.LocalAccount,
                    account.Username,
                    null,
                    account.SshPrivateKeyPath),
                cancellationToken);
            accounts++;
        }

        var known = (await store.GetConnectionIdentitiesAsync(cancellationToken))
            .Select(item => item.Id)
            .ToHashSet();

        var pins = 0;
        foreach (var pin in payload.MachinePins ?? [])
        {
            if (!Guid.TryParse(pin.LocalAccountId, out var accountId) || !known.Contains(accountId)) continue;
            if (string.IsNullOrWhiteSpace(pin.ResourceId)) continue;
            await store.SetMachinePinAsync(pin.ResourceId, accountId, cancellationToken);
            pins++;
        }

        if (payload.Settings is { } settings) await settingsStore.SaveAsync(settings, cancellationToken);
        return (accounts, pins);
    }

    public static string DefaultExportPath =>
        Path.Combine(
            StagecoachPaths.RootDirectory,
            "export",
            $"stagecoach-settings-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
}
