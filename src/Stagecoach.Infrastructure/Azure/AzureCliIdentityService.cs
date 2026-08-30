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
        CancellationToken cancellationToken = default)
    {
        var pendingId = Guid.NewGuid();
        var configDirectory = StagecoachPaths.IdentityConfigDirectory(pendingId);
        Directory.CreateDirectory(configDirectory);

        await ConfigureProfileAsync(configDirectory, cancellationToken);
        var arguments = new List<string> { "login", "--allow-no-subscriptions", "--output", "json" };
        if (useDeviceCode) arguments.Add("--use-device-code");
        var result = await cli.RunInteractiveAsync(configDirectory, arguments, cancellationToken);
        if (!result.Succeeded)
        {
            TryDeleteDirectory(Path.GetDirectoryName(configDirectory)!);
            throw new InvalidOperationException(SafeLoginError(result.StandardError));
        }

        var accountName = await ReadAuthenticatedAccountNameAsync(configDirectory, cancellationToken);
        var identityId = StableIdentityId(accountName);
        var finalDirectory = StagecoachPaths.IdentityConfigDirectory(identityId);
        if ((await store.GetIdentitiesAsync(cancellationToken)).Any(item => item.Id == identityId))
        {
            TryDeleteDirectory(Path.GetDirectoryName(configDirectory)!);
            throw new InvalidOperationException($"{accountName} is already configured. Reauthenticate the existing identity instead.");
        }
        if (!string.Equals(configDirectory, finalDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
            Directory.Move(Path.GetDirectoryName(configDirectory)!, Path.GetDirectoryName(finalDirectory)!);
        }

        var identity = new AzureIdentityProfile(
            identityId,
            string.IsNullOrWhiteSpace(displayName) ? accountName : displayName.Trim(),
            accountName,
            finalDirectory,
            AuthenticationState.Ready,
            DateTimeOffset.UtcNow);
        await store.UpsertIdentityAsync(identity, cancellationToken);
        var inventory = await RefreshInventoryAsync(identity, cancellationToken);
        await store.UpsertIdentityInventoryAsync(inventory, cancellationToken);
        return identity;
    }

    public async Task<AzureIdentityProfile> ReauthenticateAsync(
        AzureIdentityProfile identity,
        bool useDeviceCode,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "login", "--allow-no-subscriptions", "--output", "json" };
        if (useDeviceCode) arguments.Add("--use-device-code");
        var result = await cli.RunInteractiveAsync(identity.AzureConfigDirectory, arguments, cancellationToken);
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

    public async Task<IdentityInventory> RefreshInventoryAsync(
        AzureIdentityProfile identity,
        CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(
            identity.AzureConfigDirectory,
            ["account", "list", "--all", "--refresh", "--output", "json"],
            cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException("Azure subscription discovery failed. Reauthenticate this identity and retry.");

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
            var tenantName = OptionalString(element, "tenantDisplayName") ?? tenantId;
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
            throw new InvalidOperationException("Stagecoach could not initialize the isolated Azure CLI profile.");
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
            throw new InvalidOperationException("Microsoft sign-in completed, but Stagecoach could not read the signed-in account.");
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

    private static string SafeLoginError(string error) =>
        error.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            ? "Microsoft sign-in was cancelled."
            : "Microsoft sign-in failed. Use device-code sign-in or review Azure CLI authentication policy.";

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
