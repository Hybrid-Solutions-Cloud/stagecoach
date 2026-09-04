using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Azure;

public sealed class AzureCliIdentityService(IAzureCliRunner cli, IMetadataStore store) : IIdentityService
{
    public async Task<AzureIdentityProfile> AddAsync(
        string displayName,
        bool useDeviceCode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await PruneOrphanedProfilesAsync(cancellationToken);

        var pendingId = Guid.NewGuid();
        var configDirectory = StagecoachPaths.IdentityConfigDirectory(pendingId);
        Directory.CreateDirectory(configDirectory);

        await ConfigureProfileAsync(configDirectory, cancellationToken);
        var arguments = new List<string> { "login", "--allow-no-subscriptions", "--output", "json" };
        if (useDeviceCode) arguments.Add("--use-device-code");
        var result = await cli.RunInteractiveAsync(configDirectory, arguments, progress, cancellationToken);
        if (!result.Succeeded)
        {
            TryDeleteDirectory(Path.GetDirectoryName(configDirectory)!);
            throw new InvalidOperationException(SafeLoginError(result.StandardError));
        }

        var accountName = await ReadAuthenticatedAccountNameAsync(configDirectory, cancellationToken);
        var identityId = StableIdentityId(accountName);
        var finalDirectory = StagecoachPaths.IdentityConfigDirectory(identityId);
        // Signing in again with an account that is already stored is a reauthentication, not an
        // error. Refusing it was a dead end: a half-added account — saved, but never shown because
        // a later step failed — could not be re-added and could not be reauthenticated either,
        // because it was not visible to select.
        var existing = (await store.GetIdentitiesAsync(cancellationToken))
            .FirstOrDefault(item => item.Id == identityId);
        if (!string.Equals(configDirectory, finalDirectory, StringComparison.OrdinalIgnoreCase))
        {
            // The signed-in profile lives in <identities>\<pending id>\azure and has to become
            // <identities>\<stable id>\azure. Move the id-level folders, not the 'azure' folders.
            var pendingRoot = Path.GetDirectoryName(configDirectory)!;
            var finalRoot = Path.GetDirectoryName(finalDirectory)!;

            // Create the *parent* of the destination. Creating the destination itself guaranteed
            // that the move below threw "a file or directory with the same name already exists",
            // so adding an account failed every time even after a successful Microsoft sign-in.
            Directory.CreateDirectory(Path.GetDirectoryName(finalRoot)!);

            // A destination left behind by an earlier failed attempt is orphaned — the store has
            // already been checked and holds no identity with this id — so it is safe to clear.
            if (Directory.Exists(finalRoot)) TryDeleteDirectory(finalRoot);

            if (Directory.Exists(finalRoot))
                throw new InvalidOperationException(
                    $"Stagecoach could not replace the existing profile folder '{finalRoot}'. " +
                    "Close any program using it, or delete that folder, then sign in again.");

            Directory.Move(pendingRoot, finalRoot);
        }

        var identity = new AzureIdentityProfile(
            identityId,
            string.IsNullOrWhiteSpace(displayName)
                ? existing?.DisplayName ?? accountName
                : displayName.Trim(),
            accountName,
            finalDirectory,
            AuthenticationState.Ready,
            DateTimeOffset.UtcNow);
        await store.UpsertIdentityAsync(identity, cancellationToken);

        // Enumerating tenants and subscriptions is a separate Azure call and can fail on its own —
        // an account with no subscriptions, a transient error, Conditional Access. It must not
        // discard an account that has just signed in successfully: doing so left the identity
        // saved but invisible, and blocked every later attempt with "already configured".
        try
        {
            var inventory = await RefreshInventoryAsync(identity, cancellationToken);
            await store.UpsertIdentityInventoryAsync(inventory, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return identity with
            {
                LastErrorCategory = "subscription_discovery_failed",
            };
        }

        return identity;
    }

    public async Task<AzureIdentityProfile> ReauthenticateAsync(
        AzureIdentityProfile identity,
        bool useDeviceCode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "login", "--allow-no-subscriptions", "--output", "json" };
        if (useDeviceCode) arguments.Add("--use-device-code");
        var result = await cli.RunInteractiveAsync(identity.AzureConfigDirectory, arguments, progress, cancellationToken);
        if (!result.Succeeded)
            return identity with
            {
                AuthenticationState = AuthenticationState.InteractionRequired,
                LastErrorCategory = "interactive_authentication_failed",
            };

        var returnedAccount = await ReadAuthenticatedAccountNameAsync(identity.AzureConfigDirectory, cancellationToken);
        if (!string.Equals(returnedAccount, identity.AccountName, StringComparison.OrdinalIgnoreCase))
        {
            await cli.RunAsync(identity.AzureConfigDirectory, ["account", "clear"], cancellationToken);
            throw new InvalidOperationException("Microsoft returned a different account than the selected Stagecoach identity.");
        }

        var updated = identity with
        {
            AuthenticationState = AuthenticationState.Ready,
            LastAuthenticatedAt = DateTimeOffset.UtcNow,
            LastErrorCategory = null,
        };
        await store.UpsertIdentityAsync(updated, cancellationToken);
        return updated;
    }

    /// <summary>
    /// Signs in for a single connection. Nothing is written to the metadata store, so the account
    /// never becomes a connected identity and the profile can be discarded afterwards.
    /// </summary>
    public async Task<AzureIdentityProfile> SignInTransientAsync(
        string configDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        Directory.CreateDirectory(configDirectory);
        await ConfigureProfileAsync(configDirectory, cancellationToken);

        var result = await cli.RunInteractiveAsync(
            configDirectory, ["login", "--allow-no-subscriptions", "--output", "json"], progress, cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException(SafeLoginError(result.StandardError));

        var accountName = await ReadAuthenticatedAccountNameAsync(configDirectory, cancellationToken);
        return new AzureIdentityProfile(
            Guid.NewGuid(),
            accountName,
            accountName,
            configDirectory,
            AuthenticationState.Ready,
            DateTimeOffset.UtcNow);
    }

    public async Task<IdentityInventory> RefreshInventoryAsync(
        AzureIdentityProfile identity,
        CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(
            identity.AzureConfigDirectory,
            ["account", "list", "--all", "--refresh", "--output", "json"],
            cancellationToken);
        if (!result.Succeeded)
        {
            var detail = FirstMeaningfulLine(result.StandardError);
            throw new InvalidOperationException(
                "Azure subscription discovery failed." +
                (detail is null ? string.Empty : $" Azure CLI reported: {detail}") +
                " The account is still connected — use Refresh available scope to try again.");
        }

        var existingTenants = (await store.GetTenantsAsync(identity.Id, cancellationToken))
            .ToDictionary(item => item.TenantId, StringComparer.OrdinalIgnoreCase);
        var existingSubscriptions = (await store.GetSubscriptionsAsync(identity.Id, cancellationToken))
            .ToDictionary(item => item.SubscriptionId, StringComparer.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var tenants = new Dictionary<string, TenantScope>(StringComparer.OrdinalIgnoreCase);
        var subscriptions = new List<SubscriptionScope>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var tenantId = RequiredString(element, "tenantId");

            // Nobody recognises a tenant by its GUID. Take the display name, then the default
            // domain, and only fall back to the identifier when the CLI offers neither.
            var tenantName =
                OptionalString(element, "tenantDisplayName") ??
                OptionalString(element, "tenantDefaultDomain") ??
                tenantId;
            if (!tenants.ContainsKey(tenantId))
            {
                var known = existingTenants.GetValueOrDefault(tenantId);
                tenants[tenantId] = new TenantScope(
                    identity.Id,
                    tenantId,
                    tenantName,
                    known?.IsEnabled ?? false,
                    known is null);
            }

            var subscriptionId = RequiredString(element, "id");
            var knownSubscription = existingSubscriptions.GetValueOrDefault(subscriptionId);
            subscriptions.Add(new SubscriptionScope(
                identity.Id,
                tenantId,
                subscriptionId,
                OptionalString(element, "name") ?? subscriptionId,
                OptionalString(element, "state") ?? "Unknown",
                knownSubscription?.IsEnabled ?? false,
                knownSubscription is null));
        }

        return new IdentityInventory(identity, tenants.Values.OrderBy(item => item.DisplayName).ToArray(),
            subscriptions.OrderBy(item => item.DisplayName).ToArray());
    }

    public async Task RemoveAsync(AzureIdentityProfile identity, CancellationToken cancellationToken = default)
    {
        await cli.RunAsync(identity.AzureConfigDirectory, ["account", "clear"], cancellationToken);
        await store.RemoveIdentityAsync(identity.Id, cancellationToken);
        TryDeleteDirectory(Path.GetDirectoryName(identity.AzureConfigDirectory)!);
    }

    private async Task ConfigureProfileAsync(string directory, CancellationToken cancellationToken)
    {
        var result = await cli.RunAsync(directory,
            ["config", "set", "core.login_experience_v2=off", "core.collect_telemetry=false",
             "core.only_show_errors=true", "core.no_color=true"], cancellationToken);
        if (!result.Succeeded)
        {
            // This is the first write into the isolated profile, so it is where an unwritable or
            // read-only directory shows up. Say which directory, and what the CLI actually said.
            var detail = FirstMeaningfulLine(result.StandardError);
            throw new InvalidOperationException(
                $"Stagecoach could not initialize the isolated Azure CLI profile at '{directory}'." +
                (detail is null ? string.Empty : $" Azure CLI reported: {detail}"));
        }
    }

    private static string ReadAccountName(string json)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var subscription in document.RootElement.EnumerateArray())
        {
            if (subscription.TryGetProperty("user", out var user) &&
                user.TryGetProperty("name", out var name) &&
                !string.IsNullOrWhiteSpace(name.GetString()))
                return name.GetString()!;
        }
        throw new InvalidOperationException("Azure sign-in completed but returned no account identity.");
    }

    private async Task<string> ReadAuthenticatedAccountNameAsync(string configDirectory, CancellationToken cancellationToken)
    {
        var accounts = await cli.RunAsync(configDirectory, ["account", "list", "--all", "--output", "json"], cancellationToken);
        if (!accounts.Succeeded)
        {
            var detail = FirstMeaningfulLine(accounts.StandardError);
            throw new InvalidOperationException(
                "Microsoft sign-in completed, but Stagecoach could not read the signed-in account." +
                (detail is null ? string.Empty : $" Azure CLI reported: {detail}"));
        }
        return ReadAccountName(accounts.StandardOutput);
    }

    internal static Guid StableIdentityId(string accountName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(accountName.Trim().ToUpperInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new JsonException($"Azure CLI output omitted {property}.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Turns an Azure CLI sign-in failure into something an operator can act on. The CLI's own
    /// message is included, because collapsing every failure into one sentence made real problems
    /// (unwritable profile directories, broker policy, proxy refusals) impossible to tell apart.
    /// Tokens are already stripped by the runner; this additionally caps the length.
    /// </summary>
    private static string SafeLoginError(string error)
    {
        if (error.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            return "Microsoft sign-in was cancelled.";

        var detail = FirstMeaningfulLine(error);
        return detail is null
            ? "Microsoft sign-in failed. Use device-code sign-in or review Azure CLI authentication policy."
            : $"Microsoft sign-in failed: {detail} Try device-code sign-in, or run 'az login' in a terminal to see the full Azure CLI output.";
    }

    private static string? FirstMeaningfulLine(string error)
    {
        var line = error
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(candidate =>
                candidate.Length > 0 &&
                !candidate.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("Traceback", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("  File \"", StringComparison.Ordinal));
        if (line is null) return null;
        return line.Length <= 300 ? line : string.Concat(line.AsSpan(0, 300), "…");
    }

    /// <summary>
    /// Removes profile folders that no stored identity points at. An abandoned or failed sign-in
    /// leaves one behind, and they otherwise accumulate silently, each holding an Azure CLI token
    /// cache that nothing will ever use again.
    /// </summary>
    private async Task PruneOrphanedProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(StagecoachPaths.IdentityDirectory)) return;
            var known = (await store.GetIdentitiesAsync(cancellationToken))
                .Select(item => item.Id.ToString("D"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in Directory.GetDirectories(StagecoachPaths.IdentityDirectory))
            {
                if (known.Contains(Path.GetFileName(directory))) continue;
                TryDeleteDirectory(directory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Housekeeping only; never block a sign-in because a stale folder could not be removed.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
