namespace Stagecoach.Core;

public enum AuthenticationState
{
    Unknown,
    Ready,
    InteractionRequired,
    Disabled,
    Error,
}

public enum MachineKind
{
    AzureVm,
    ArcServer,
    AzureLocalVm,
}

public enum OperatingSystemKind
{
    Unknown,
    Windows,
    Linux,
}

public enum ConnectionRouteKind
{
    DirectRdp,
    DirectSsh,
    BastionRdp,
    BastionTunnelRdp,
    BastionSsh,
    ArcRdp,
    ArcSsh,
}

public enum ReadinessState
{
    Ready,
    InteractionRequired,
    MissingPrerequisite,
    Offline,
    PermissionDenied,
    Unsupported,
    Unknown,
}

public enum ConnectionIdentityKind
{
    ActiveDirectory,
    LocalAccount,
    MicrosoftEntra,
    SshKey,
    PromptOnly,
}

public enum MappingScopeKind
{
    Tenant,
    Subscription,
    ResourceGroup,
    Domain,
    Tag,
    Machine,
}

public enum SessionState
{
    Starting,
    Active,
    InteractionRequired,
    Failed,
    Stopping,
    Stopped,
}

public sealed record AzureIdentityProfile(
    Guid Id,
    string DisplayName,
    string AccountName,
    string AzureConfigDirectory,
    AuthenticationState AuthenticationState,
    DateTimeOffset? LastAuthenticatedAt,
    bool IsEnabled = true,
    string? LastErrorCategory = null);

public sealed record TenantScope(
    Guid IdentityId,
    string TenantId,
    string DisplayName,
    bool IsEnabled,
    bool RequiresReview = false);

public sealed record SubscriptionScope(
    Guid IdentityId,
    string TenantId,
    string SubscriptionId,
    string DisplayName,
    string State,
    bool IsEnabled,
    bool RequiresReview = false);

public sealed record AzureAccessPath(
    Guid IdentityId,
    string TenantId,
    string SubscriptionId,
    ConnectionRouteKind Route,
    ReadinessState Readiness,
    string Reason,
    string? BastionResourceId = null,
    bool IsPreferred = false);

public sealed record MachineRecord(
    string ResourceId,
    string Name,
    MachineKind Kind,
    OperatingSystemKind OperatingSystem,
    string OperatingSystemName,
    string ResourceGroup,
    string Location,
    string PowerState,
    string AgentState,
    string? PrivateIpAddress,
    string? PublicIpAddress,
    string? VirtualNetworkId,
    string? DomainName,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<AzureAccessPath> AccessPaths,
    DateTimeOffset LastDiscoveredAt,
    /// <summary>
    /// True when the machine carries the Entra login extension, so a work account can sign in to
    /// Windows or Linux on it. False means the in-guest account is local or domain — which decides
    /// whether a local account has to be pinned before connecting.
    /// </summary>
    bool SupportsEntraLogin = false,
    bool IsFavorite = false,
    DateTimeOffset? LastConnectedAt = null)
{
    public string StableKey => ResourceId.ToUpperInvariant();
    public bool IsWindows => OperatingSystem == OperatingSystemKind.Windows;
    public ReadinessState BestReadiness =>
        AccessPaths.OrderBy(path => path.Readiness == ReadinessState.Ready ? 0 : 1)
            .ThenBy(path => path.IsPreferred ? 0 : 1)
            .Select(path => path.Readiness)
            .FirstOrDefault(ReadinessState.Unknown);
}

public sealed record ConnectionIdentityProfile(
    Guid Id,
    string DisplayName,
    ConnectionIdentityKind Kind,
    string Username,
    string? CredentialTarget,
    string? SshPrivateKeyPath,
    bool IsEnabled = true);

public sealed record ConnectionIdentityMapping(
    Guid Id,
    Guid ConnectionIdentityId,
    MappingScopeKind ScopeKind,
    string MatchValue,
    int Priority,
    bool IsRelayIdentity = false);

public sealed record ConnectionSession(
    Guid Id,
    string MachineResourceId,
    string MachineName,
    ConnectionRouteKind Route,
    Guid AzureIdentityId,
    Guid? ConnectionIdentityId,
    DateTimeOffset StartedAt,
    SessionState State,
    int? HelperProcessId = null,
    int? ClientProcessId = null,
    int? LocalPort = null,
    string? SafeStatus = null);

public sealed record IdentityInventory(
    AzureIdentityProfile Identity,
    IReadOnlyList<TenantScope> Tenants,
    IReadOnlyList<SubscriptionScope> Subscriptions);

public sealed record DiscoveryResult(
    Guid IdentityId,
    IReadOnlyList<MachineRecord> Machines,
    DateTimeOffset CompletedAt,
    IReadOnlyList<string> SafeWarnings);

public enum AuditCategory
{
    Identity,
    Scope,
    Discovery,
    Connection,
    Remediation,
    Update,

    // Appended, never reordered: the stored value is the number, and existing rows keep theirs.

    /// <summary>Opening, unlocking, closing — and any action that failed.</summary>
    Application,

    /// <summary>Local accounts, pins, and other estate edits made by hand.</summary>
    Estate,
}

/// <summary>
/// One recorded action, for the activity log. Deliberately free of credentials, tokens, and
/// resource identifiers: it answers "what happened and when", not "what is in the estate".
/// </summary>
public sealed record AuditEvent(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditCategory Category,
    string Summary,
    string? Detail = null);

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed record WorkstationReadiness(
    bool IsWindows,
    bool HasAzureCli,
    Version? AzureCliVersion,
    bool HasSshExtension,
    Version? SshExtensionVersion,
    bool HasBastionCommands,
    bool HasOpenSsh,
    bool HasMstsc,
    IReadOnlyList<string> Actions)
{
    public bool CanDiscover => IsWindows && HasAzureCli;
}

public sealed record RemediationAction(
    string Id,
    string Title,
    string Description,
    string TargetResourceId,
    Guid AzureIdentityId,
    IReadOnlyList<string> SafeOperations,
    bool RequiresAzureWrite);
