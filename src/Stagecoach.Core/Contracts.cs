namespace Stagecoach.Core;

public interface IAzureCliRunner
{
    Task<CommandResult> RunAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    Task<CommandResult> RunInteractiveAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    Task<IManagedCommand> StartBackgroundAsync(
        string azureConfigDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);
}

public interface IManagedCommand : IAsyncDisposable
{
    int ProcessId { get; }
    Task<int> Completion { get; }
    IReadOnlyList<string> GetSafeOutput();
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IIdentityService
{
    Task<AzureIdentityProfile> AddAsync(
        string displayName,
        bool useDeviceCode,
        CancellationToken cancellationToken = default);

    Task<AzureIdentityProfile> ReauthenticateAsync(
        AzureIdentityProfile identity,
        bool useDeviceCode,
        CancellationToken cancellationToken = default);

    Task<IdentityInventory> RefreshInventoryAsync(
        AzureIdentityProfile identity,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(AzureIdentityProfile identity, CancellationToken cancellationToken = default);
}

public interface IMetadataStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzureIdentityProfile>> GetIdentitiesAsync(CancellationToken cancellationToken = default);
    Task UpsertIdentityAsync(AzureIdentityProfile identity, CancellationToken cancellationToken = default);
    Task RemoveIdentityAsync(Guid identityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantScope>> GetTenantsAsync(Guid identityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionScope>> GetSubscriptionsAsync(Guid identityId, CancellationToken cancellationToken = default);
    Task UpsertIdentityInventoryAsync(IdentityInventory inventory, CancellationToken cancellationToken = default);
    Task SetTenantEnabledAsync(Guid identityId, string tenantId, bool enabled, CancellationToken cancellationToken = default);
    Task SetSubscriptionEnabledAsync(Guid identityId, string subscriptionId, bool enabled, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MachineRecord>> GetMachinesAsync(CancellationToken cancellationToken = default);
    Task UpsertDiscoveryAsync(DiscoveryResult result, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(string resourceId, bool favorite, CancellationToken cancellationToken = default);
    Task RecordConnectionAsync(string resourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectionIdentityProfile>> GetConnectionIdentitiesAsync(CancellationToken cancellationToken = default);
    Task UpsertConnectionIdentityAsync(ConnectionIdentityProfile profile, CancellationToken cancellationToken = default);
    Task RemoveConnectionIdentityAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectionIdentityMapping>> GetConnectionMappingsAsync(CancellationToken cancellationToken = default);
    Task UpsertConnectionMappingAsync(ConnectionIdentityMapping mapping, CancellationToken cancellationToken = default);
    Task RemoveConnectionMappingAsync(Guid mappingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Local accounts pinned to a specific machine, keyed by upper-case resource ID. A pinned
    /// machine never asks the operator which account to use.
    /// </summary>
    Task<IReadOnlyDictionary<string, Guid>> GetMachinePinsAsync(CancellationToken cancellationToken = default);

    /// <summary>Pins a local account to a machine, or clears the pin when <paramref name="connectionIdentityId"/> is null.</summary>
    Task SetMachinePinAsync(string resourceId, Guid? connectionIdentityId, CancellationToken cancellationToken = default);
}

public interface IEstateDiscoveryService
{
    Task<DiscoveryResult> DiscoverAsync(
        AzureIdentityProfile identity,
        IReadOnlyList<SubscriptionScope> subscriptions,
        CancellationToken cancellationToken = default);
}

public interface IConnectionCredentialStore
{
    Task SaveAsync(Guid profileId, string username, string password, CancellationToken cancellationToken = default);
    Task<(string Username, string Password)?> ReadAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
    string GetTargetName(Guid profileId);
}

public interface IConnectionService
{
    Task<ConnectionSession> ConnectAsync(
        MachineRecord machine,
        AzureAccessPath accessPath,
        AzureIdentityProfile azureIdentity,
        ConnectionIdentityProfile? targetIdentity,
        ConnectionIdentityProfile? relayIdentity,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConnectionSession>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task StopAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public interface IWorkstationReadinessService
{
    Task<WorkstationReadiness> InspectAsync(CancellationToken cancellationToken = default);
    Task PrepareCliExtensionsAsync(CancellationToken cancellationToken = default);
}

public interface IArcRemediationService
{
    RemediationAction PreviewOpenSshInstallation(MachineRecord machine, AzureAccessPath accessPath);
    Task ApplyOpenSshInstallationAsync(
        RemediationAction action,
        MachineRecord machine,
        AzureAccessPath accessPath,
        AzureIdentityProfile identity,
        CancellationToken cancellationToken = default);
}
