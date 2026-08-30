using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagecoach.Core;
using Stagecoach.Infrastructure.Orchestration;

namespace Stagecoach.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IMetadataStore _store;
    private readonly IIdentityService _identityService;
    private readonly IEstateDiscoveryService _discovery;
    private readonly IConnectionCredentialStore _credentialStore;
    private readonly IConnectionService _connections;
    private readonly IWorkstationReadinessService _readiness;
    private readonly IArcRemediationService _arcRemediation;
    private readonly AppSettingsStore _settingsStore;
    private readonly List<MachineRecord> _allMachines = [];
    private readonly List<ConnectionIdentityMapping> _mappings = [];

    public MainViewModel(
        IMetadataStore store,
        IIdentityService identityService,
        IEstateDiscoveryService discovery,
        IConnectionCredentialStore credentialStore,
        IConnectionService connections,
        IWorkstationReadinessService readiness,
        IArcRemediationService arcRemediation,
        AppSettingsStore settingsStore)
    {
        _store = store;
        _identityService = identityService;
        _discovery = discovery;
        _credentialStore = credentialStore;
        _connections = connections;
        _readiness = readiness;
        _arcRemediation = arcRemediation;
        _settingsStore = settingsStore;
        foreach (var kind in Enum.GetValues<ConnectionIdentityKind>()) ConnectionIdentityKinds.Add(kind);
        foreach (var theme in Enum.GetValues<AppTheme>()) Themes.Add(theme);
        foreach (var accent in Enum.GetValues<AppAccent>()) Accents.Add(accent);
        foreach (var behavior in Enum.GetValues<CloseBehavior>()) CloseBehaviors.Add(behavior);
        foreach (var kind in Enum.GetValues<MappingScopeKind>()) MappingKinds.Add(kind);
    }

    public ObservableCollection<IdentityRow> Identities { get; } = [];
    public ObservableCollection<TenantRow> Tenants { get; } = [];
    public ObservableCollection<SubscriptionRow> Subscriptions { get; } = [];
    public ObservableCollection<MachineRow> Machines { get; } = [];
    public ObservableCollection<ConnectionIdentityRow> ConnectionIdentities { get; } = [];
    public ObservableCollection<SessionRow> Sessions { get; } = [];
    public ObservableCollection<ConnectionIdentityKind> ConnectionIdentityKinds { get; } = [];
    public ObservableCollection<AppTheme> Themes { get; } = [];
    public ObservableCollection<AppAccent> Accents { get; } = [];
    public ObservableCollection<CloseBehavior> CloseBehaviors { get; } = [];
    public ObservableCollection<MappingScopeKind> MappingKinds { get; } = [];
    public ObservableCollection<ConnectionMappingRow> ConnectionMappings { get; } = [];

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Starting";
    [ObservableProperty] private string _workstationStatus = "Checking workstation";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IdentityRow? _selectedIdentity;
    [ObservableProperty] private MachineRow? _selectedMachine;
    [ObservableProperty] private string _newIdentityName = string.Empty;
    [ObservableProperty] private string _newConnectionName = string.Empty;
    [ObservableProperty] private string _newConnectionUsername = string.Empty;
    [ObservableProperty] private string _newConnectionPassword = string.Empty;
    [ObservableProperty] private string _newConnectionSshKeyPath = string.Empty;
    [ObservableProperty] private ConnectionIdentityKind _newConnectionKind = ConnectionIdentityKind.ActiveDirectory;
    [ObservableProperty] private Guid? _editingConnectionIdentityId;
    [ObservableProperty] private MappingScopeKind _newMappingKind = MappingScopeKind.Domain;
    [ObservableProperty] private string _newMappingValue = string.Empty;
    [ObservableProperty] private int _newMappingPriority;
    [ObservableProperty] private bool _newMappingIsRelay;
    [ObservableProperty] private ConnectionIdentityRow? _selectedConnectionIdentity;
    [ObservableProperty] private AppTheme _selectedTheme = AppTheme.System;
    [ObservableProperty] private AppAccent _selectedAccent = AppAccent.Rust;
    [ObservableProperty] private CloseBehavior _selectedCloseBehavior = CloseBehavior.NotificationArea;
    [ObservableProperty] private bool _minimizeToNotificationArea = true;
    [ObservableProperty] private bool _backgroundSyncEnabled = true;
    [ObservableProperty] private int _backgroundSyncMinutes = 30;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private RemediationAction? _pendingRemediation;

    public bool HasIdentities => Identities.Count > 0;
    public bool HasMachines => Machines.Count > 0;

    public async Task InitializeAsync()
    {
        await RunBusyAsync("Opening encrypted local estate", async () =>
        {
            await _store.InitializeAsync();
            var settings = await _settingsStore.LoadAsync();
            SelectedTheme = settings.Theme;
            SelectedAccent = settings.Accent;
            SelectedCloseBehavior = settings.CloseBehavior;
            MinimizeToNotificationArea = settings.MinimizeToNotificationArea;
            BackgroundSyncEnabled = settings.BackgroundSyncEnabled;
            BackgroundSyncMinutes = Math.Clamp(settings.BackgroundSyncMinutes, 5, 1440);
            StartMinimized = settings.StartMinimized;

            var readiness = await _readiness.InspectAsync();
            WorkstationStatus = readiness.CanDiscover
                ? readiness.Actions.Count == 0 ? "Workstation ready" : string.Join("  •  ", readiness.Actions)
                : "Azure CLI is required before Stagecoach can discover machines.";
            await ReloadIdentitiesAsync();
            await ReloadMachinesAsync();
            await ReloadConnectionIdentitiesAsync();
            await ReloadSessionsAsync();
            SelectedTabIndex = Identities.Count == 0 ? 1 : 0;
            StatusMessage = Identities.Count == 0 ? "Add your first Entra identity" : $"{Machines.Count} machines cached";
        });
    }

    [RelayCommand]
    private Task AddWindowsAccountAsync() => AddIdentityAsync(useDeviceCode: false);

    [RelayCommand]
    private Task AddDeviceCodeAccountAsync() => AddIdentityAsync(useDeviceCode: true);

    private async Task AddIdentityAsync(bool useDeviceCode)
    {
        await RunBusyAsync("Signing in with Microsoft", async () =>
        {
            var identity = await _identityService.AddAsync(NewIdentityName, useDeviceCode);
            NewIdentityName = string.Empty;
            await ReloadIdentitiesAsync();
            SelectedIdentity = Identities.FirstOrDefault(item => item.Profile.Id == identity.Id);
            StatusMessage = $"{identity.DisplayName} connected. Select tenants and subscriptions.";
        });
    }

    [RelayCommand]
    private async Task ReauthenticateAsync(IdentityRow? row)
    {
        if (row is null) return;
        await RunBusyAsync($"Reauthenticating {row.DisplayName}", async () =>
        {
            await _identityService.ReauthenticateAsync(row.Profile, useDeviceCode: false);
            await ReloadIdentitiesAsync();
        });
    }

    [RelayCommand]
    private async Task RemoveIdentityAsync(IdentityRow? row)
    {
        if (row is null) return;
        await RunBusyAsync($"Removing {row.DisplayName}", async () =>
        {
            await _identityService.RemoveAsync(row.Profile);
            await ReloadIdentitiesAsync();
            await ReloadMachinesAsync();
        });
    }

    [RelayCommand]
    private async Task RefreshIdentityScopeAsync()
    {
        if (SelectedIdentity is null) return;
        await RunBusyAsync($"Refreshing scope for {SelectedIdentity.DisplayName}", async () =>
        {
            var inventory = await _identityService.RefreshInventoryAsync(SelectedIdentity.Profile);
            await _store.UpsertIdentityInventoryAsync(inventory);
            await LoadScopeAsync(SelectedIdentity.Profile.Id);
            StatusMessage = "New scope is disabled until you explicitly enable it.";
        });
    }

    [RelayCommand]
    private async Task ToggleTenantAsync(TenantRow? row)
    {
        if (row is null || SelectedIdentity is null) return;
        await _store.SetTenantEnabledAsync(SelectedIdentity.Profile.Id, row.TenantId, !row.IsEnabled);
        await LoadScopeAsync(SelectedIdentity.Profile.Id);
    }

    [RelayCommand]
    private async Task ToggleSubscriptionAsync(SubscriptionRow? row)
    {
        if (row is null || SelectedIdentity is null) return;
        await _store.SetSubscriptionEnabledAsync(SelectedIdentity.Profile.Id, row.SubscriptionId, !row.IsEnabled);
        await LoadScopeAsync(SelectedIdentity.Profile.Id);
    }

    [RelayCommand]
    public async Task SyncEstateAsync()
    {
        await RunBusyAsync("Scanning selected Azure estates", async () =>
        {
            var failures = new List<string>();
            foreach (var row in Identities.Where(item => item.Profile.IsEnabled))
            {
                try
                {
                    var subscriptions = await _store.GetSubscriptionsAsync(row.Profile.Id);
                    var result = await _discovery.DiscoverAsync(row.Profile, subscriptions);
                    await _store.UpsertDiscoveryAsync(result);
                }
                catch (Exception)
                {
                    failures.Add(row.DisplayName);
                }
            }
            await ReloadMachinesAsync();
            StatusMessage = failures.Count == 0
                ? $"Sync complete — {Machines.Count} machines"
                : $"Sync completed with isolated errors for: {string.Join(", ", failures)}";
        });
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(MachineRow? row)
    {
        if (row is null) return;
        await _store.SetFavoriteAsync(row.Machine.ResourceId, !row.Machine.IsFavorite);
        await ReloadMachinesAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync(MachineRow? row)
    {
        if (row is null) return;
        var path = row.SelectedPath;
        if (path is null) { StatusMessage = "No connection route is available."; return; }
        var azureIdentity = Identities.Select(item => item.Profile).FirstOrDefault(item => item.Id == path.IdentityId);
        if (azureIdentity is null) { StatusMessage = "The Azure identity for this route is unavailable."; return; }
        var profiles = ConnectionIdentities.Select(item => item.Profile).ToArray();
        var target = ConnectionIdentityMatcher.Select(row.Machine, path, profiles, _mappings, relayIdentity: false);
        var relay = ConnectionIdentityMatcher.Select(row.Machine, path, profiles, _mappings, relayIdentity: true);
        var needsTarget = path.Route is ConnectionRouteKind.DirectRdp or ConnectionRouteKind.BastionTunnelRdp or ConnectionRouteKind.ArcRdp;
        if (needsTarget && target is null)
        {
            SelectedTabIndex = 2;
            StatusMessage = "Create and map a connection identity for this machine or domain first.";
            return;
        }
        await RunBusyAsync($"Connecting to {row.Name}", async () =>
        {
            await _connections.ConnectAsync(row.Machine, path, azureIdentity, target, relay);
            await ReloadSessionsAsync();
            StatusMessage = $"Connection started for {row.Name}";
        });
    }

    [RelayCommand]
    private void PreviewArcRemediation(MachineRow? row)
    {
        if (row?.SelectedPath is not { } path || row.Machine.Kind is MachineKind.AzureVm)
        {
            StatusMessage = "Select a Windows Arc or Azure Local machine first.";
            return;
        }
        try
        {
            PendingRemediation = _arcRemediation.PreviewOpenSshInstallation(row.Machine, path);
            SelectedMachine = row;
            StatusMessage = "Review the Azure write below, then choose Apply only if you approve it.";
        }
        catch (InvalidOperationException exception) { StatusMessage = exception.Message; }
    }

    [RelayCommand]
    private async Task ApplyArcRemediationAsync()
    {
        if (PendingRemediation is null || SelectedMachine?.SelectedPath is not { } path) return;
        var identity = Identities.Select(item => item.Profile).FirstOrDefault(item => item.Id == path.IdentityId);
        if (identity is null) { StatusMessage = "The Azure identity for this remediation is unavailable."; return; }
        var action = PendingRemediation;
        await RunBusyAsync($"Preparing {SelectedMachine.Name}", async () =>
        {
            await _arcRemediation.ApplyOpenSshInstallationAsync(action, SelectedMachine.Machine, path, identity);
            PendingRemediation = null;
            StatusMessage = "WindowsOpenSSH deployment submitted. Sync the estate after Azure reports completion.";
        });
    }

    [RelayCommand]
    private void CancelRemediation()
    {
        PendingRemediation = null;
        StatusMessage = "No Azure changes were made.";
    }

    [RelayCommand]
    private async Task PrepareWorkstationAsync()
    {
        await RunBusyAsync("Installing or updating local Azure CLI extensions", async () =>
        {
            await _readiness.PrepareCliExtensionsAsync();
            var result = await _readiness.InspectAsync();
            WorkstationStatus = result.Actions.Count == 0 ? "Workstation ready" : string.Join("  •  ", result.Actions);
            StatusMessage = "Local Azure CLI prerequisites are ready.";
        });
    }

    [RelayCommand]
    private async Task SaveConnectionIdentityAsync()
    {
        if (string.IsNullOrWhiteSpace(NewConnectionName) || string.IsNullOrWhiteSpace(NewConnectionUsername))
        {
            StatusMessage = "Connection identity name and username are required.";
            return;
        }
        await RunBusyAsync("Saving connection identity", async () =>
        {
            var id = EditingConnectionIdentityId ?? Guid.NewGuid();
            var hasPassword = !string.IsNullOrEmpty(NewConnectionPassword);
            var existing = ConnectionIdentities.Select(item => item.Profile).FirstOrDefault(item => item.Id == id);
            var profile = new ConnectionIdentityProfile(id, NewConnectionName.Trim(), NewConnectionKind,
                NewConnectionUsername.Trim(), hasPassword ? _credentialStore.GetTargetName(id) : existing?.CredentialTarget,
                string.IsNullOrWhiteSpace(NewConnectionSshKeyPath) ? null : NewConnectionSshKeyPath.Trim());
            await _store.UpsertConnectionIdentityAsync(profile);
            if (hasPassword) await _credentialStore.SaveAsync(id, profile.Username, NewConnectionPassword);
            else if (existing?.CredentialTarget is not null && await _credentialStore.ReadAsync(id) is { } saved &&
                     !string.Equals(saved.Username, profile.Username, StringComparison.Ordinal))
                await _credentialStore.SaveAsync(id, profile.Username, saved.Password);
            NewConnectionPassword = string.Empty;
            NewConnectionName = string.Empty;
            NewConnectionUsername = string.Empty;
            NewConnectionSshKeyPath = string.Empty;
            EditingConnectionIdentityId = null;
            await ReloadConnectionIdentitiesAsync();
            SelectedConnectionIdentity = ConnectionIdentities.FirstOrDefault(item => item.Profile.Id == id);
            StatusMessage = hasPassword ? "Connection identity saved in Windows Credential Manager." : "Connection identity metadata saved.";
        });
    }

    [RelayCommand]
    private void EditConnectionIdentity(ConnectionIdentityRow? row)
    {
        if (row is null) return;
        EditingConnectionIdentityId = row.Profile.Id;
        NewConnectionName = row.Profile.DisplayName;
        NewConnectionKind = row.Profile.Kind;
        NewConnectionUsername = row.Profile.Username;
        NewConnectionSshKeyPath = row.Profile.SshPrivateKeyPath ?? string.Empty;
        NewConnectionPassword = string.Empty;
        StatusMessage = "Editing connection identity. Leave password blank to keep the stored password.";
    }

    [RelayCommand]
    private void CancelConnectionIdentityEdit()
    {
        EditingConnectionIdentityId = null;
        NewConnectionName = NewConnectionUsername = NewConnectionPassword = NewConnectionSshKeyPath = string.Empty;
        StatusMessage = "Edit cancelled.";
    }

    [RelayCommand]
    private async Task SaveMappingAsync()
    {
        if (SelectedConnectionIdentity is null || string.IsNullOrWhiteSpace(NewMappingValue)) return;
        var mapping = new ConnectionIdentityMapping(Guid.NewGuid(), SelectedConnectionIdentity.Profile.Id,
            NewMappingKind, NewMappingValue.Trim(), NewMappingPriority, NewMappingIsRelay);
        await _store.UpsertConnectionMappingAsync(mapping);
        _mappings.Add(mapping);
        NewMappingValue = string.Empty;
        await ReloadMappingsAsync();
        StatusMessage = "Connection identity mapping saved.";
    }

    [RelayCommand]
    private async Task RemoveMappingAsync(ConnectionMappingRow? row)
    {
        if (row is null) return;
        await _store.RemoveConnectionMappingAsync(row.Mapping.Id);
        await ReloadMappingsAsync();
        StatusMessage = "Connection identity mapping removed.";
    }

    [RelayCommand]
    private async Task RemoveConnectionIdentityAsync(ConnectionIdentityRow? row)
    {
        if (row is null) return;
        await _credentialStore.DeleteAsync(row.Profile.Id);
        await _store.RemoveConnectionIdentityAsync(row.Profile.Id);
        await ReloadConnectionIdentitiesAsync();
    }

    [RelayCommand]
    private async Task StopSessionAsync(SessionRow? row)
    {
        if (row is null) return;
        await _connections.StopAsync(row.Session.Id);
        await ReloadSessionsAsync();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(new AppSettings(SelectedTheme, SelectedAccent, MinimizeToNotificationArea,
            SelectedCloseBehavior, BackgroundSyncEnabled, Math.Clamp(BackgroundSyncMinutes, 5, 1440), StartMinimized));
        StatusMessage = "Settings saved.";
    }

    partial void OnSearchTextChanged(string value) => ApplyMachineFilter();
    partial void OnSelectedIdentityChanged(IdentityRow? value)
    {
        if (value is not null) _ = LoadScopeAsync(value.Profile.Id);
    }

    private async Task ReloadIdentitiesAsync()
    {
        var selectedId = SelectedIdentity?.Profile.Id;
        Identities.Clear();
        foreach (var identity in await _store.GetIdentitiesAsync()) Identities.Add(new IdentityRow(identity));
        SelectedIdentity = Identities.FirstOrDefault(item => item.Profile.Id == selectedId) ?? Identities.FirstOrDefault();
        OnPropertyChanged(nameof(HasIdentities));
    }

    private async Task LoadScopeAsync(Guid identityId)
    {
        Tenants.Clear();
        foreach (var item in await _store.GetTenantsAsync(identityId)) Tenants.Add(new TenantRow(item));
        Subscriptions.Clear();
        foreach (var item in await _store.GetSubscriptionsAsync(identityId)) Subscriptions.Add(new SubscriptionRow(item));
    }

    private async Task ReloadMachinesAsync()
    {
        _allMachines.Clear();
        _allMachines.AddRange(await _store.GetMachinesAsync());
        ApplyMachineFilter();
        OnPropertyChanged(nameof(HasMachines));
    }

    private void ApplyMachineFilter()
    {
        var query = SearchText.Trim();
        var filtered = _allMachines.Where(machine => string.IsNullOrWhiteSpace(query) ||
            machine.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            machine.ResourceGroup.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (machine.DomainName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            machine.AccessPaths.Any(path => path.SubscriptionId.Contains(query, StringComparison.OrdinalIgnoreCase)));
        Machines.Clear();
        foreach (var machine in filtered.OrderByDescending(item => item.IsFavorite).ThenBy(item => item.Name))
            Machines.Add(new MachineRow(machine));
    }

    private async Task ReloadConnectionIdentitiesAsync()
    {
        ConnectionIdentities.Clear();
        foreach (var profile in await _store.GetConnectionIdentitiesAsync()) ConnectionIdentities.Add(new ConnectionIdentityRow(profile));
        _mappings.Clear();
        _mappings.AddRange(await _store.GetConnectionMappingsAsync());
        await ReloadMappingsAsync();
    }

    private async Task ReloadMappingsAsync()
    {
        _mappings.Clear();
        _mappings.AddRange(await _store.GetConnectionMappingsAsync());
        var profiles = ConnectionIdentities.ToDictionary(item => item.Profile.Id, item => item.DisplayName);
        ConnectionMappings.Clear();
        foreach (var mapping in _mappings)
            ConnectionMappings.Add(new ConnectionMappingRow(mapping, profiles.GetValueOrDefault(mapping.ConnectionIdentityId, "Missing identity")));
    }

    private async Task ReloadSessionsAsync()
    {
        Sessions.Clear();
        foreach (var session in await _connections.GetSessionsAsync()) Sessions.Add(new SessionRow(session));
    }

    private async Task RunBusyAsync(string message, Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = message;
        try { await operation(); }
        catch (Exception exception) { StatusMessage = SafeMessage(exception); }
        finally { IsBusy = false; }
    }

    private static string SafeMessage(Exception exception) => exception switch
    {
        OperationCanceledException => "Operation cancelled.",
        InvalidOperationException => exception.Message,
        _ => "Stagecoach encountered an unexpected local error. Review diagnostics and retry.",
    };
}

public sealed record IdentityRow(AzureIdentityProfile Profile)
{
    public string DisplayName => Profile.DisplayName;
    public string AccountName => Profile.AccountName;
    public string State => Profile.AuthenticationState.ToString();
}

public sealed record TenantRow(TenantScope Scope)
{
    public string TenantId => Scope.TenantId;
    public string DisplayName => Scope.DisplayName;
    public bool IsEnabled => Scope.IsEnabled;
    public string Selection => IsEnabled ? "Included" : Scope.RequiresReview ? "Review" : "Excluded";
}

public sealed record SubscriptionRow(SubscriptionScope Scope)
{
    public string SubscriptionId => Scope.SubscriptionId;
    public string DisplayName => Scope.DisplayName;
    public string State => Scope.State;
    public bool IsEnabled => Scope.IsEnabled;
    public string Selection => IsEnabled ? "Included" : Scope.RequiresReview ? "Review" : "Excluded";
}

public partial class MachineRow : ObservableObject
{
    public MachineRow(MachineRecord machine)
    {
        Machine = machine;
        Paths = machine.AccessPaths.Select(path => new AccessPathRow(path)).ToArray();
        SelectedRoute = Paths.FirstOrDefault(item => item.Path.IsPreferred) ?? Paths.FirstOrDefault();
    }
    public MachineRecord Machine { get; }
    public IReadOnlyList<AccessPathRow> Paths { get; }
    [ObservableProperty] private AccessPathRow? _selectedRoute;
    public AzureAccessPath? SelectedPath => SelectedRoute?.Path;
    partial void OnSelectedRouteChanged(AccessPathRow? value)
    {
        OnPropertyChanged(nameof(SelectedPath));
        OnPropertyChanged(nameof(Route));
        OnPropertyChanged(nameof(Readiness));
    }
    public string Name => Machine.Name;
    public string Kind => Machine.Kind switch { MachineKind.AzureVm => "Azure VM", MachineKind.ArcServer => "Azure Arc", _ => "Azure Local" };
    public string OperatingSystem => Machine.OperatingSystem.ToString();
    public string Environment => Machine.DomainName ?? "Unmapped";
    public string State => string.IsNullOrWhiteSpace(Machine.AgentState) ? Machine.PowerState : Machine.AgentState;
    public string Identity => Machine.AccessPaths.Select(item => item.IdentityId.ToString("N")[..8]).FirstOrDefault() ?? "None";
    public string Route => SelectedPath?.Route.ToString() ?? "None";
    public string Readiness => SelectedPath?.Readiness.ToString() ?? Machine.BestReadiness.ToString();
    public string Favorite => Machine.IsFavorite ? "★" : "☆";
}

public sealed record AccessPathRow(AzureAccessPath Path)
{
    public override string ToString() => $"{Path.Route} — {Path.Readiness}";
}

public sealed record ConnectionIdentityRow(ConnectionIdentityProfile Profile)
{
    public string DisplayName => Profile.DisplayName;
    public string Username => Profile.Username;
    public string Kind => Profile.Kind.ToString();
    public string Credential => Profile.CredentialTarget is null ? "No stored password" : "Windows Credential Manager";
}

public sealed record ConnectionMappingRow(ConnectionIdentityMapping Mapping, string IdentityName)
{
    public string Scope => Mapping.ScopeKind.ToString();
    public string Match => Mapping.MatchValue;
    public string Purpose => Mapping.IsRelayIdentity ? "Arc relay" : "Target login";
    public int Priority => Mapping.Priority;
}

public sealed record SessionRow(ConnectionSession Session)
{
    public string Machine => Session.MachineName;
    public string Route => Session.Route.ToString();
    public string State => Session.State.ToString();
    public string Started => Session.StartedAt.LocalDateTime.ToString("g");
}
