using Stagecoach.Core.Models;

namespace Stagecoach.Core.Interfaces;

public interface IDiscoveryService
{
    Task<IReadOnlyList<StagecoachIdentity>> GetIdentitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StagecoachMachine>> DiscoverEstateAsync(IEnumerable<string>? tenantIds = null, CancellationToken cancellationToken = default);
    Task<bool> TriggerInteractiveLoginAsync(string? tenantId = null, string? usernameHint = null, CancellationToken cancellationToken = default);
}

public interface ICredentialResolver
{
    Task<CredentialResolution> ResolveCredentialAsync(StagecoachMachine target, string vaultName = "kv-hcs-vault-01", CancellationToken cancellationToken = default);
    Task<bool> SaveWorkgroupSecretAsync(string machineName, string password, string vaultName = "kv-hcs-vault-01", CancellationToken cancellationToken = default);
}

public interface IMetadataStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StagecoachMachine>> GetAllMachinesAsync(CancellationToken cancellationToken = default);
    Task SaveMachinesAsync(IEnumerable<StagecoachMachine> machines, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(string machineId, bool isFavorite, CancellationToken cancellationToken = default);
    Task RecordConnectionAsync(string machineId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StagecoachMachine>> GetRecentMachinesAsync(int count = 10, CancellationToken cancellationToken = default);
}

public interface IProcessOrchestrator
{
    Task<StagecoachSession> ConnectAsync(StagecoachMachine machine, string username, string? password = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StagecoachSession>> GetActiveSessionsAsync();
    Task DisconnectSessionAsync(string sessionId);
}
