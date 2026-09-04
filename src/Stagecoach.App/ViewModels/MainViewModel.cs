using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagecoach.App.Security;
using Stagecoach.Core;
using Stagecoach.Infrastructure;
using Stagecoach.Infrastructure.Storage;

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
    private readonly IReleaseUpdateService _updates;
    private readonly AppSettingsStore _settingsStore;
    private readonly List<MachineRecord> _allMachines = [];
    private readonly Dictionary<string, Guid> _pins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tenantNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _subscriptionNames = new(StringComparer.OrdinalIgnoreCase);
    private VerifiedReleaseUpdate? _verifiedUpdate;

    public MainViewModel(
        IMetadataStore store,
        IIdentityService identityService,
        IEstateDiscoveryService discovery,
        IConnectionCredentialStore credentialStore,
        IConnectionService connections,
        IWorkstationReadinessService readiness,
        IArcRemediationService arcRemediation,
        IReleaseUpdateService updates,
        AppSettingsStore settingsStore)
    {
        _store = store;
        _identityService = identityService;
        _discovery = discovery;
        _credentialStore = credentialStore;
        _connections = connections;
        _readiness = readiness;
        _arcRemediation = arcRemediation;
        _updates = updates;
        _settingsStore = settingsStore;
        foreach (var theme in Enum.GetValues<AppTheme>()) Themes.Add(theme);
        foreach (var accent in Enum.GetValues<AppAccent>()) Accents.Add(accent);
        foreach (var behavior in Enum.GetValues<CloseBehavior>()) CloseBehaviors.Add(behavior);
        ResetFilterOptions();
    }

    public ObservableCollection<IdentityRow> Identities { get; } = [];
    public ObservableCollection<TenantRow> Tenants { get; } = [];
    public ObservableCollection<SubscriptionRow> Subscriptions { get; } = [];
    public ObservableCollection<MachineRow> Machines { get; } = [];
    public ObservableCollection<LocalAccountRow> LocalAccounts { get; } = [];
    public ObservableCollection<SessionRow> Sessions { get; } = [];
    public ObservableCollection<AppTheme> Themes { get; } = [];
    public ObservableCollection<AppAccent> Accents { get; } = [];
    public ObservableCollection<CloseBehavior> CloseBehaviors { get; } = [];
    public ObservableCollection<FilterOption> TenantFilters { get; } = [];
    public ObservableCollection<FilterOption> SubscriptionFilters { get; } = [];
    public ObservableCollection<FilterOption> SourceFilters { get; } = [];
    public ObservableCollection<FilterOption> OperatingSystemFilters { get; } = [];
    public ObservableCollection<FilterOption> StateFilters { get; } = [];

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Starting";
    [ObservableProperty] private string _workstationStatus = "Checking workstation";
    [ObservableProperty] private string _errorTitle = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasActionableError;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IdentityRow? _selectedIdentity;
    [ObservableProperty] private MachineRow? _selectedMachine;
    [ObservableProperty] private string _newIdentityName = string.Empty;
    [ObservableProperty] private string _renameIdentityText = string.Empty;

    [ObservableProperty] private FilterOption? _selectedTenantFilter;
    [ObservableProperty] private FilterOption? _selectedSubscriptionFilter;
    [ObservableProperty] private FilterOption? _selectedSourceFilter;
    [ObservableProperty] private FilterOption? _selectedOperatingSystemFilter;
    [ObservableProperty] private FilterOption? _selectedStateFilter;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _readyOnly;
    [ObservableProperty] private bool _pinnedOnly;

    [ObservableProperty] private string _newAccountName = string.Empty;
    [ObservableProperty] private string _newAccountUsername = string.Empty;
    [ObservableProperty] private string _newAccountPassword = string.Empty;
    [ObservableProperty] private Guid? _editingAccountId;

    [ObservableProperty] private bool _isAccountPickerOpen;
    [ObservableProperty] private MachineRow? _pickerMachine;
    [ObservableProperty] private LocalAccountRow? _pickerAccount;
    [ObservableProperty] private bool _pickerRemember = true;

    [ObservableProperty] private bool _isMachineEditorOpen;
    [ObservableProperty] private MachineRow? _editorMachine;
    [ObservableProperty] private LocalAccountRow? _editorAccount;
    [ObservableProperty] private AccessPathRow? _editorRoute;

    [ObservableProperty] private AppTheme _selectedTheme = AppTheme.System;
    [ObservableProperty] private AppAccent _selectedAccent = AppAccent.Rust;
    [ObservableProperty] private CloseBehavior _selectedCloseBehavior = CloseBehavior.NotificationArea;
    [ObservableProperty] private bool _minimizeToNotificationArea = true;
    [ObservableProperty] private bool _backgroundSyncEnabled = true;
    [ObservableProperty] private int _backgroundSyncMinutes = 30;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private RemediationAction? _pendingRemediation;

    [ObservableProperty] private string _currentVersion = "0.0.0";
    [ObservableProperty] private string _updateStatus = "Updates have not been checked.";
    [ObservableProperty] private ReleaseUpdateInfo? _availableUpdate;
    [ObservableProperty] private bool _isUpdateReadyToInstall;
    [ObservableProperty] private string _supportBundleStatus = "No support bundle has been created yet.";
    [ObservableProperty] private string _latestSupportBundlePath = string.Empty;

    private WorkstationReadiness? _lastReadiness;

    /// <summary>
    /// True until at least one Microsoft Entra account is connected. Drives the first-run guidance,
    /// because an operator opening Stagecoach for the first time otherwise lands on an empty list
    /// with nothing telling them what to do.
    /// </summary>
    public bool IsFirstRun => Identities.Count == 0;

    public string SetupAccountMarker => Identities.Count == 0 ? "1" : "✓";
    public string SetupAccountStatus => Identities.Count switch
    {
        0 => "Not started",
        1 => "1 account connected",
        var count => $"{count} accounts connected",
    };

    public string SetupScopeMarker => IncludedSubscriptionCount > 0 ? "✓" : "2";
    public string SetupScopeStatus => Identities.Count == 0
        ? "Connect an account first"
        : IncludedSubscriptionCount > 0
            ? $"{IncludedSubscriptionCount} subscription(s) included"
            : "Nothing included yet";

    public string SetupScanMarker => _allMachines.Count > 0 ? "✓" : "3";
    public string SetupScanStatus => _allMachines.Count > 0
        ? $"{_allMachines.Count} machines found"
        : IncludedSubscriptionCount > 0 ? "Ready to scan" : "Include scope first";

    private int IncludedSubscriptionCount => Subscriptions.Count(row => row.IsEnabled);

    private void RefreshSetupState()
    {
        OnPropertyChanged(nameof(IsFirstRun));
        OnPropertyChanged(nameof(SetupAccountMarker));
        OnPropertyChanged(nameof(SetupAccountStatus));
        OnPropertyChanged(nameof(SetupScopeMarker));
        OnPropertyChanged(nameof(SetupScopeStatus));
        OnPropertyChanged(nameof(SetupScanMarker));
        OnPropertyChanged(nameof(SetupScanStatus));
    }

    [RelayCommand]
    private void GoToConnectIdentities() => SelectedTabIndex = 1;

    [RelayCommand]
    private void GoToMachines() => SelectedTabIndex = 0;

    public bool HasIdentities => Identities.Count > 0;
    public bool HasMachines => Machines.Count > 0;
    public bool HasLocalAccounts => LocalAccounts.Count > 0;
    public int ActiveSessionCount => Sessions.Count(row =>
        row.Session.State is SessionState.Starting or SessionState.Active or SessionState.InteractionRequired);
    public string EstateSummary => _allMachines.Count == Machines.Count
        ? $"{Machines.Count} machines"
        : $"{Machines.Count} of {_allMachines.Count} machines";
    public string ActiveIdentityContext => SelectedIdentity is null
        ? "No Entra account connected"
        : $"{SelectedIdentity.DisplayName} · {SelectedIdentity.AccountName}";

    public async Task InitializeAsync()
    {
        CurrentVersion = typeof(MainViewModel).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";
        await RunBusyAsync("Opening encrypted local estate", async () =>
        {
            await _store.InitializeAsync();

            // Recorded before anything else can fail. Putting this after the reloads meant a single
            // failure anywhere in startup left the Activity page completely empty, which reads as
            // the log being broken rather than as the thing that actually went wrong.
            await RecordAsync(AuditCategory.Identity, "Stagecoach opened", $"Version {CurrentVersion}");

            var settings = await _settingsStore.LoadAsync();
            SelectedTheme = settings.Theme;
            SelectedAccent = settings.Accent;
            SelectedCloseBehavior = settings.CloseBehavior;
            MinimizeToNotificationArea = settings.MinimizeToNotificationArea;
            BackgroundSyncEnabled = settings.BackgroundSyncEnabled;
            BackgroundSyncMinutes = Math.Clamp(settings.BackgroundSyncMinutes, 5, 1440);
            StartMinimized = settings.StartMinimized;

            // Readiness runs several Azure CLI commands and takes tens of seconds. It used to run
            // inside the busy gate, so every control — including the sign-in buttons — stayed
            // disabled until it finished and the application looked broken on launch. It now runs
            // in the background and fills in its status when it arrives.
            WorkstationStatus = "Checking workstation prerequisites…";
            _ = InspectWorkstationInBackgroundAsync();

            await ReloadIdentitiesAsync();
            await ReloadLocalAccountsAsync();
            await ReloadMachinesAsync();
            await ReloadSessionsAsync();
            await RefreshAuditAsync();

            // The machine list is always the landing screen. An operator with no identity yet is
            // told what to do rather than being dropped into a settings surface.
            SelectedTabIndex = 0;
            StatusMessage = Identities.Count == 0
                ? "Add an Entra account under Connect identities to discover machines."
                : $"{Machines.Count} machines cached";
        });
    }

    private async Task InspectWorkstationInBackgroundAsync()
    {
        try
        {
            var readiness = await _readiness.InspectAsync();
            _lastReadiness = readiness;
            WorkstationStatus = readiness.CanDiscover
                ? readiness.Actions.Count == 0 ? "Workstation ready" : string.Join("  •  ", readiness.Actions)
                : "Azure CLI is required before Stagecoach can discover machines.";
        }
        catch (Exception exception)
        {
            CrashLog.Record("Workstation readiness", exception);
            WorkstationStatus = SafeMessage(exception);
        }
    }

    // ---------------------------------------------------------------- Entra identities

    [RelayCommand]
    private Task AddWindowsAccountAsync() => AddIdentityAsync(useDeviceCode: false);

    [RelayCommand]
    private Task AddDeviceCodeAccountAsync() => AddIdentityAsync(useDeviceCode: true);

    private async Task AddIdentityAsync(bool useDeviceCode)
    {
        // Device codes and sign-in URLs arrive while the sign-in is still open, so they have to be
        // shown as they happen. Previously they went to a hidden console and the operator saw nothing.
        var progress = new Progress<string>(line => StatusMessage = line);
        await RunBusyAsync(
            useDeviceCode
                ? "Starting device-code sign-in — the code will appear here"
                : "Signing in with Microsoft — complete the prompt Windows shows", async () =>
        {
            try
            {
                var identity = await _identityService.AddAsync(NewIdentityName, useDeviceCode, progress);
                NewIdentityName = string.Empty;
                SelectedIdentity = Identities.FirstOrDefault(item => item.Profile.Id == identity.Id);
                StatusMessage = $"{identity.DisplayName} connected. Choose the tenants and subscriptions to scan.";
                SelectedTabIndex = 1;

                // Enumerating scope is part of adding an account, not a separate chore. When the
                // attempt inside AddAsync failed, retry it here so the operator does not have to
                // discover "Refresh available scope" on their own — and so the real reason reaches
                // the error banner and the log instead of being swallowed.
                if (identity.LastErrorCategory == "subscription_discovery_failed")
                {
                    StatusMessage = $"{identity.DisplayName} connected. Listing tenants and subscriptions…";
                    var inventory = await _identityService.RefreshInventoryAsync(identity);
                    await _store.UpsertIdentityInventoryAsync(inventory);
                    StatusMessage = $"{identity.DisplayName} connected. Choose the tenants and subscriptions to scan.";
                }

                await RecordAsync(
                    AuditCategory.Identity,
                    $"Connected {identity.DisplayName}",
                    useDeviceCode ? "Device-code sign-in" : "Interactive sign-in");
            }
            finally
            {
                // Always reload. An account can be stored and still fail a later step, and leaving
                // the list stale made it invisible — which then blocked every retry as a duplicate.
                await ReloadIdentitiesAsync();
                SelectedIdentity ??= Identities.FirstOrDefault();
            }
        });
    }

    [RelayCommand]
    private async Task ReauthenticateAsync(IdentityRow? row)
    {
        if (row is null) return;
        var progress = new Progress<string>(line => StatusMessage = line);
        await RunBusyAsync($"Reauthenticating {row.DisplayName}", async () =>
        {
            await _identityService.ReauthenticateAsync(row.Profile, useDeviceCode: false, progress);
            await ReloadIdentitiesAsync();
            await RecordAsync(AuditCategory.Identity, $"Reauthenticated {row.DisplayName}");
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
            await RecordAsync(AuditCategory.Identity, $"Removed {row.DisplayName}");
        });
    }

    /// <summary>
    /// Re-enumerates tenants and subscriptions so newly granted scope becomes visible. New scope
    /// is never scanned until the operator includes it.
    /// </summary>
    [RelayCommand]
    private async Task RefreshIdentityScopeAsync()
    {
        if (SelectedIdentity is null) { StatusMessage = "Select an Entra account first."; return; }
        var identity = SelectedIdentity;
        await RunBusyAsync($"Refreshing available scope for {identity.DisplayName}", async () =>
        {
            var inventory = await _identityService.RefreshInventoryAsync(identity.Profile);
            await _store.UpsertIdentityInventoryAsync(inventory);
            await LoadScopeAsync(identity.Profile.Id);
            await RecordAsync(
                AuditCategory.Scope,
                $"Refreshed available scope for {identity.DisplayName}",
                $"{inventory.Tenants.Count} tenant(s), {inventory.Subscriptions.Count} subscription(s) visible");
            StatusMessage = "New tenants and subscriptions stay excluded until you include them.";
        });
    }

    /// <summary>Rescans machines inside the scope already included for one identity.</summary>
    [RelayCommand]
    private async Task RescanIdentityAsync()
    {
        if (SelectedIdentity is null) { StatusMessage = "Select an Entra account first."; return; }
        var identity = SelectedIdentity;
        await RunBusyAsync($"Rescanning machines for {identity.DisplayName}", async () =>
        {
            var subscriptions = await GetScannableSubscriptionsAsync(identity.Profile.Id);
            await RecordAsync(
                AuditCategory.Discovery,
                $"Scan started for {identity.DisplayName}",
                $"{subscriptions.Count} subscription(s) in scope");
            try
            {
                var result = await _discovery.DiscoverAsync(identity.Profile, subscriptions);
                await _store.UpsertDiscoveryAsync(result);
                await ReloadMachinesAsync();
                var detail = $"{subscriptions.Count} subscription(s) in scope, {result.Machines.Count} machine(s) found";
                await RecordAsync(
                    AuditCategory.Discovery,
                    $"Scan finished for {identity.DisplayName}",
                    result.SafeWarnings.Count == 0 ? detail : $"{detail}, {result.SafeWarnings.Count} warning(s)");
                StatusMessage = $"{identity.DisplayName} rescanned — {Machines.Count} machines";
            }
            catch (Exception exception)
            {
                // A scan that fails is the entry an operator most needs to see. Recording only
                // success meant the Activity page stayed empty exactly when something was wrong.
                await RecordAsync(
                    AuditCategory.Discovery,
                    $"Scan failed for {identity.DisplayName}",
                    exception.Message);
                throw;
            }
        });
    }

    /// <summary>Renames the selected account. Only the local label changes; the Azure account does not.</summary>
    [RelayCommand]
    private async Task RenameIdentityAsync()
    {
        if (SelectedIdentity is not { } row) { StatusMessage = "Select an account first."; return; }
        var name = RenameIdentityText.Trim();
        if (name.Length == 0) { StatusMessage = "Enter a name for this account."; return; }

        await RunBusyAsync($"Renaming {row.DisplayName}", async () =>
        {
            await _store.UpsertIdentityAsync(row.Profile with { DisplayName = name });
            var id = row.Profile.Id;
            await ReloadIdentitiesAsync();
            SelectedIdentity = Identities.FirstOrDefault(item => item.Profile.Id == id);
            RenameIdentityText = string.Empty;
            StatusMessage = $"Renamed to {name}.";
        });
    }

    [RelayCommand]
    private Task IncludeAllTenantsAsync() => SetAllTenantsAsync(true);

    [RelayCommand]
    private Task ExcludeAllTenantsAsync() => SetAllTenantsAsync(false);

    [RelayCommand]
    private Task IncludeAllSubscriptionsAsync() => SetAllSubscriptionsAsync(true);

    [RelayCommand]
    private Task ExcludeAllSubscriptionsAsync() => SetAllSubscriptionsAsync(false);

    private async Task SetAllTenantsAsync(bool enabled)
    {
        if (SelectedIdentity is not { } row) { StatusMessage = "Select an account first."; return; }
        var identityId = row.Profile.Id;
        await RunBusyAsync(enabled ? "Including every tenant" : "Excluding every tenant", async () =>
        {
            foreach (var tenant in Tenants.ToArray())
                await _store.SetTenantEnabledAsync(identityId, tenant.TenantId, enabled);
            await LoadScopeAsync(identityId);
            StatusMessage = enabled
                ? $"All {Tenants.Count} tenants included."
                : "All tenants excluded.";
        });
    }

    private async Task SetAllSubscriptionsAsync(bool enabled)
    {
        if (SelectedIdentity is not { } row) { StatusMessage = "Select an account first."; return; }
        var identityId = row.Profile.Id;
        await RunBusyAsync(enabled ? "Including every subscription" : "Excluding every subscription", async () =>
        {
            foreach (var subscription in Subscriptions.ToArray())
                await _store.SetSubscriptionEnabledAsync(identityId, subscription.SubscriptionId, enabled);
            await LoadScopeAsync(identityId);
            StatusMessage = enabled
                ? $"All {Subscriptions.Count} subscriptions included. Rescan machines to discover them."
                : "All subscriptions excluded.";
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
        await RunBusyAsync("Scanning included Azure scope", async () =>
        {
            var failures = new List<string>();
            foreach (var row in Identities.Where(item => item.Profile.IsEnabled))
            {
                try
                {
                    var subscriptions = await GetScannableSubscriptionsAsync(row.Profile.Id);
                    var result = await _discovery.DiscoverAsync(row.Profile, subscriptions);
                    await _store.UpsertDiscoveryAsync(result);
                }
                catch (Exception)
                {
                    // One identity's failure never blocks the rest of the estate.
                    failures.Add(row.DisplayName);
                }
            }

            await ReloadMachinesAsync();
            if (failures.Count == 0)
            {
                StatusMessage = $"Sync complete — {Machines.Count} machines";
            }
            else
            {
                StatusMessage = $"Sync completed with isolated errors for: {string.Join(", ", failures)}";
                RaiseError("Some accounts could not be scanned",
                    $"These accounts need attention: {string.Join(", ", failures)}. Reauthenticate them under Connect identities, then rescan.");
            }
        });
    }

    // ---------------------------------------------------------------- Machines

    [RelayCommand]
    private async Task ToggleFavoriteAsync(MachineRow? row)
    {
        if (row is null) return;
        await _store.SetFavoriteAsync(row.Machine.ResourceId, !row.Machine.IsFavorite);
        await ReloadMachinesAsync();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        FavoritesOnly = false;
        ReadyOnly = false;
        PinnedOnly = false;
        SelectedTenantFilter = TenantFilters.FirstOrDefault();
        SelectedSubscriptionFilter = SubscriptionFilters.FirstOrDefault();
        SelectedSourceFilter = SourceFilters.FirstOrDefault();
        SelectedOperatingSystemFilter = OperatingSystemFilters.FirstOrDefault();
        SelectedStateFilter = StateFilters.FirstOrDefault();
        ApplyMachineFilter();
    }

    /// <summary>Opens the per-machine editor, where a local account can be pinned ahead of time.</summary>
    [RelayCommand]
    private void EditMachine(MachineRow? row)
    {
        if (row is null) return;
        EditorMachine = row;
        EditorAccount = row.PinnedAccountId is { } pinned
            ? LocalAccounts.FirstOrDefault(item => item.Profile.Id == pinned)
            : null;
        EditorRoute = row.SelectedRoute;
        IsMachineEditorOpen = true;
        StatusMessage = LocalAccounts.Count == 0
            ? "Add a local account first — Local accounts in the left navigation."
            : $"Pin the local account Stagecoach should always use for {row.Name}.";
    }

    [RelayCommand]
    private async Task SaveMachineEditAsync()
    {
        if (EditorMachine is not { } row) return;
        var accountId = EditorAccount?.Profile.Id;
        await _store.SetMachinePinAsync(row.Machine.ResourceId, accountId);
        if (EditorRoute is not null) row.SelectedRoute = EditorRoute;
        IsMachineEditorOpen = false;
        await ReloadMachinesAsync();
        StatusMessage = accountId is null
            ? $"{row.Name} will ask which local account to use."
            : $"{EditorAccount!.DisplayName} pinned to {row.Name}. It will not ask again.";
    }

    [RelayCommand]
    private void CancelMachineEdit()
    {
        IsMachineEditorOpen = false;
        EditorMachine = null;
        EditorAccount = null;
    }

    /// <summary>
    /// One click from the estate list. A pinned machine connects immediately; an unpinned one asks
    /// which stored local account to use, and remembers the answer.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync(MachineRow? row)
    {
        if (row is null) return;
        if (row.SelectedPath is not { } path)
        {
            RaiseError("No route is available", $"{row.Name} has no usable connection route from any connected account.");
            return;
        }

        if (!RouteNeedsLocalAccount(path.Route))
        {
            await LaunchAsync(row, path, account: null);
            return;
        }

        if (row.PinnedAccountId is { } pinnedId &&
            LocalAccounts.FirstOrDefault(item => item.Profile.Id == pinnedId) is { } pinned)
        {
            await LaunchAsync(row, path, pinned);
            return;
        }

        if (LocalAccounts.Count == 0)
        {
            SelectedTabIndex = 2;
            RaiseError("No local account is stored",
                "Add the machine's local administrator account under Local accounts, then connect again.");
            return;
        }

        PickerMachine = row;
        PickerAccount = LocalAccounts.FirstOrDefault();
        PickerRemember = true;
        IsAccountPickerOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmAccountPickAsync()
    {
        if (PickerMachine is not { } row || PickerAccount is not { } account) return;
        IsAccountPickerOpen = false;
        if (PickerRemember)
        {
            await _store.SetMachinePinAsync(row.Machine.ResourceId, account.Profile.Id);
            _pins[row.Machine.StableKey] = account.Profile.Id;
            row.ApplyPin(account.Profile.Id, account.DisplayName);
        }

        if (row.SelectedPath is { } path) await LaunchAsync(row, path, account);
    }

    [RelayCommand]
    private void CancelAccountPick()
    {
        IsAccountPickerOpen = false;
        PickerMachine = null;
        StatusMessage = "Connection cancelled.";
    }

    /// <summary>
    /// Arc RDP relays SSH then RDP. Both hops use the same stored local account, so the operator is
    /// never asked to enter a local administrator account for Arc.
    /// </summary>
    private async Task LaunchAsync(MachineRow row, AzureAccessPath path, LocalAccountRow? account)
    {
        var azureIdentity = Identities
            .Select(item => item.Profile)
            .FirstOrDefault(item => item.Id == path.IdentityId);
        if (azureIdentity is null)
        {
            RaiseError("That Entra account is unavailable",
                $"The account that discovered {row.Name} is no longer connected. Reconnect it under Connect identities.");
            return;
        }

        await RunBusyAsync($"Connecting to {row.Name}", async () =>
        {
            await _connections.ConnectAsync(row.Machine, path, azureIdentity, account?.Profile, account?.Profile);
            await ReloadSessionsAsync();
            await RecordAsync(
                AuditCategory.Connection,
                $"Connected to {row.Name}",
                $"{DescribeRoute(path.Route)}; local account {account?.DisplayName ?? "not required"}");
            StatusMessage = $"{row.Name} — {DescribeRoute(path.Route)} started";
        });
    }

    private static bool RouteNeedsLocalAccount(ConnectionRouteKind route) =>
        route is ConnectionRouteKind.DirectRdp
            or ConnectionRouteKind.BastionTunnelRdp
            or ConnectionRouteKind.ArcRdp
            or ConnectionRouteKind.ArcSsh
            or ConnectionRouteKind.DirectSsh;

    // ---------------------------------------------------------------- Local accounts

    [RelayCommand]
    private async Task SaveLocalAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAccountName) || string.IsNullOrWhiteSpace(NewAccountUsername))
        {
            StatusMessage = "A display name and username are required.";
            return;
        }

        await RunBusyAsync("Saving local account", async () =>
        {
            var id = EditingAccountId ?? Guid.NewGuid();
            var hasPassword = !string.IsNullOrEmpty(NewAccountPassword);
            var existing = LocalAccounts.Select(item => item.Profile).FirstOrDefault(item => item.Id == id);
            var username = NewAccountUsername.Trim();
            var profile = new ConnectionIdentityProfile(
                id,
                NewAccountName.Trim(),
                username.Contains('\\') || username.Contains('@')
                    ? ConnectionIdentityKind.ActiveDirectory
                    : ConnectionIdentityKind.LocalAccount,
                username,
                hasPassword ? _credentialStore.GetTargetName(id) : existing?.CredentialTarget,
                existing?.SshPrivateKeyPath);
            await _store.UpsertConnectionIdentityAsync(profile);
            if (hasPassword)
            {
                await _credentialStore.SaveAsync(id, profile.Username, NewAccountPassword);
            }
            else if (existing?.CredentialTarget is not null &&
                     await _credentialStore.ReadAsync(id) is { } saved &&
                     !string.Equals(saved.Username, profile.Username, StringComparison.Ordinal))
            {
                await _credentialStore.SaveAsync(id, profile.Username, saved.Password);
            }

            NewAccountPassword = string.Empty;
            NewAccountName = string.Empty;
            NewAccountUsername = string.Empty;
            EditingAccountId = null;
            await ReloadLocalAccountsAsync();
            await ReloadMachinesAsync();
            StatusMessage = hasPassword
                ? $"{profile.DisplayName} saved. The password is in Windows Credential Manager."
                : $"{profile.DisplayName} saved.";
        });
    }

    [RelayCommand]
    private void EditLocalAccount(LocalAccountRow? row)
    {
        if (row is null) return;
        EditingAccountId = row.Profile.Id;
        NewAccountName = row.Profile.DisplayName;
        NewAccountUsername = row.Profile.Username;
        NewAccountPassword = string.Empty;
        StatusMessage = "Leave the password blank to keep the stored password.";
    }

    [RelayCommand]
    private void CancelLocalAccountEdit()
    {
        EditingAccountId = null;
        NewAccountName = NewAccountUsername = NewAccountPassword = string.Empty;
        StatusMessage = "Edit cancelled.";
    }

    [RelayCommand]
    private async Task RemoveLocalAccountAsync(LocalAccountRow? row)
    {
        if (row is null) return;
        var pinnedTo = _pins.Count(pair => pair.Value == row.Profile.Id);
        await RunBusyAsync($"Removing {row.DisplayName}", async () =>
        {
            await _credentialStore.DeleteAsync(row.Profile.Id);
            await _store.RemoveConnectionIdentityAsync(row.Profile.Id);
            await ReloadLocalAccountsAsync();
            await ReloadMachinesAsync();
            StatusMessage = pinnedTo == 0
                ? $"{row.DisplayName} removed."
                : $"{row.DisplayName} removed. {pinnedTo} machine(s) will ask which account to use.";
        });
    }

    // ---------------------------------------------------------------- Sessions, readiness, updates

    [RelayCommand]
    private async Task StopSessionAsync(SessionRow? row)
    {
        if (row is null) return;
        await _connections.StopAsync(row.Session.Id);
        await ReloadSessionsAsync();
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
            StatusMessage = "Review the Azure write below, then apply only if you approve it.";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ApplyArcRemediationAsync()
    {
        if (PendingRemediation is null || SelectedMachine?.SelectedPath is not { } path) return;
        var identity = Identities.Select(item => item.Profile).FirstOrDefault(item => item.Id == path.IdentityId);
        if (identity is null) { StatusMessage = "The Azure identity for this remediation is unavailable."; return; }
        var action = PendingRemediation;
        var machine = SelectedMachine.Machine;
        await RunBusyAsync($"Preparing {machine.Name}", async () =>
        {
            await _arcRemediation.ApplyOpenSshInstallationAsync(action, machine, path, identity);
            PendingRemediation = null;
            StatusMessage = "WindowsOpenSSH deployment submitted. Rescan once Azure reports completion.";
        });
    }

    [RelayCommand]
    private void CancelRemediation()
    {
        PendingRemediation = null;
        StatusMessage = "No Azure changes were made.";
    }

    /// <summary>
    /// Collects the error log, an environment summary, and a local-state inventory into one zip an
    /// operator can attach to a support request. Nothing from the database, the isolated Azure CLI
    /// profiles, or Windows Credential Manager is included.
    /// </summary>
    // ---------------------------------------------------------------- Quick connect

    /// <summary>
    /// A one-off connection that persists nothing: no identity, no pin, no scope, no estate row.
    /// It queries Resource Graph directly for the chosen scope and connects to what is picked.
    /// </summary>
    public ObservableCollection<TenantRow> QuickTenants { get; } = [];
    public ObservableCollection<SubscriptionRow> QuickSubscriptions { get; } = [];
    public ObservableCollection<MachineRow> QuickResults { get; } = [];
    public ObservableCollection<string> QuickRouteKinds { get; } = ["Azure Bastion", "Azure Arc"];

    [ObservableProperty] private bool _isQuickConnectOpen;
    [ObservableProperty] private TenantRow? _quickTenant;
    [ObservableProperty] private SubscriptionRow? _quickSubscription;
    [ObservableProperty] private string _quickRouteKind = "Azure Bastion";
    [ObservableProperty] private string _quickResourceName = string.Empty;
    [ObservableProperty] private MachineRow? _quickSelected;
    [ObservableProperty] private string _quickStatus = string.Empty;

    /// <summary>An Entra-login machine needs no in-guest account; anything else does.</summary>
    public bool QuickNeedsLocalAccount => QuickSelected is null || !QuickSelected.Machine.SupportsEntraLogin;

    partial void OnQuickSelectedChanged(MachineRow? value) => OnPropertyChanged(nameof(QuickNeedsLocalAccount));

    /// <summary>
    /// The throwaway Azure CLI profile Quick Connect signs in to. It is deleted when the dialog
    /// closes, so a quick connection leaves no connected identity behind.
    /// </summary>
    private string? _quickProfileDirectory;
    private AzureIdentityProfile? _quickProfile;

    [ObservableProperty] private bool _quickSignedIn;
    [ObservableProperty] private string _quickAccountName = string.Empty;

    /// <summary>
    /// 1 sign in, 2 tenant, 3 subscription, 4 route, 5 resource, 6 in-guest account, 7 results.
    /// Everything comes from the live sign-in; nothing is read from or written to the local store.
    /// </summary>
    [ObservableProperty] private int _quickStep = 1;

    [ObservableProperty] private string _quickUsername = string.Empty;
    [ObservableProperty] private string _quickPassword = string.Empty;

    public bool QuickIsStep1 => QuickStep == 1;
    public bool QuickIsStep2 => QuickStep == 2;
    public bool QuickIsStep3 => QuickStep == 3;
    public bool QuickIsStep4 => QuickStep == 4;
    public bool QuickIsStep5 => QuickStep == 5;
    public bool QuickIsStep6 => QuickStep == 6;
    public bool QuickIsStep7 => QuickStep == 7;
    public bool QuickCanGoBack => QuickStep is > 2 and < 8;

    public string QuickStepTitle => QuickStep switch
    {
        1 => "Sign in",
        2 => "Which tenant?",
        3 => "Which subscription?",
        4 => "How is it reached?",
        5 => "Which machine?",
        6 => "Account to use inside the machine",
        _ => "Pick a machine",
    };

    partial void OnQuickStepChanged(int value)
    {
        OnPropertyChanged(nameof(QuickIsStep1));
        OnPropertyChanged(nameof(QuickIsStep2));
        OnPropertyChanged(nameof(QuickIsStep3));
        OnPropertyChanged(nameof(QuickIsStep4));
        OnPropertyChanged(nameof(QuickIsStep5));
        OnPropertyChanged(nameof(QuickIsStep6));
        OnPropertyChanged(nameof(QuickIsStep7));
        OnPropertyChanged(nameof(QuickCanGoBack));
        OnPropertyChanged(nameof(QuickStepTitle));
    }

    [RelayCommand]
    private void QuickBack()
    {
        if (QuickStep > 2) QuickStep--;
    }

    [RelayCommand]
    private async Task QuickNextAsync()
    {
        switch (QuickStep)
        {
            case 2 when QuickTenant is null:
                QuickStatus = "Choose a tenant.";
                return;
            case 5:
                // Search happens between choosing the machine name and choosing the account, so the
                // account step only appears once there is something to connect to.
                await QuickSearchAsync();
                if (QuickResults.Count == 0) return;
                QuickStep = 6;
                return;
            case 6:
                QuickStep = 7;
                return;
            default:
                QuickStep++;
                return;
        }
    }

    [RelayCommand]
    private void OpenQuickConnect()
    {
        DiscardQuickProfile();
        QuickResults.Clear();
        QuickSubscriptions.Clear();
        QuickTenants.Clear();
        QuickResourceName = string.Empty;
        QuickSelected = null;
        QuickSignedIn = false;
        QuickAccountName = string.Empty;
        QuickUsername = string.Empty;
        QuickPassword = string.Empty;
        QuickStep = 1;
        QuickStatus = "Sign in with the Entra account that can see the machine.";
        IsQuickConnectOpen = true;
    }

    /// <summary>
    /// Signs in for this one connection only, in a fresh isolated profile that is never stored and
    /// never becomes a connected identity.
    /// </summary>
    [RelayCommand]
    private async Task QuickSignInAsync()
    {
        var progress = new Progress<string>(line => QuickStatus = line);
        await RunBusyAsync("Signing in for a one-off connection", async () =>
        {
            DiscardQuickProfile();
            _quickProfileDirectory = Path.Combine(
                StagecoachPaths.RootDirectory, "quick", Guid.NewGuid().ToString("N"), "azure");
            Directory.CreateDirectory(_quickProfileDirectory);

            var identity = await _identityService.SignInTransientAsync(_quickProfileDirectory, progress);
            _quickProfile = identity;
            QuickAccountName = identity.AccountName;
            QuickSignedIn = true;

            QuickTenants.Clear();
            QuickSubscriptions.Clear();
            var inventory = await _identityService.RefreshInventoryAsync(identity);
            foreach (var tenant in inventory.Tenants) QuickTenants.Add(new TenantRow(tenant));
            QuickTenant = QuickTenants.FirstOrDefault();
            LoadQuickSubscriptions(inventory);
            QuickStatus = $"Signed in as {identity.AccountName}.";
            QuickStep = 2;
        });
    }

    private IdentityInventory? _quickInventory;

    private void LoadQuickSubscriptions(IdentityInventory inventory)
    {
        _quickInventory = inventory;
        QuickSubscriptions.Clear();
        if (QuickTenant is not { } tenant) return;
        foreach (var subscription in inventory.Subscriptions)
        {
            if (string.Equals(subscription.TenantId, tenant.TenantId, StringComparison.OrdinalIgnoreCase))
                QuickSubscriptions.Add(new SubscriptionRow(subscription));
        }
    }

    partial void OnQuickTenantChanged(TenantRow? value)
    {
        if (_quickInventory is { } inventory) LoadQuickSubscriptions(inventory);
    }

    private void DiscardQuickProfile()
    {
        _quickProfile = null;
        _quickInventory = null;
        if (_quickProfileDirectory is null) return;
        try
        {
            var root = Path.GetDirectoryName(_quickProfileDirectory);
            if (root is not null && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort; the folder holds only a throwaway token cache.
        }
        finally
        {
            _quickProfileDirectory = null;
        }
    }

    /// <summary>
    /// Searches the chosen scope without saving anything. With no subscription chosen it searches
    /// every subscription in the tenant; with no resource name it lists everything of the chosen
    /// kind so the operator can pick.
    /// </summary>
    [RelayCommand]
    private async Task QuickSearchAsync()
    {
        if (_quickProfile is not { } profile) { QuickStatus = "Sign in first."; return; }
        if (QuickTenant is not { } tenant) { QuickStatus = "Choose a tenant."; return; }

        await RunBusyAsync("Searching", async () =>
        {
            QuickResults.Clear();
            QuickSelected = null;

            SubscriptionScope[] scope = QuickSubscription is { } chosen
                ? [chosen.Scope with { IsEnabled = true }]
                : [.. (_quickInventory?.Subscriptions ?? [])
                    .Where(item => string.Equals(item.TenantId, tenant.TenantId, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item with { IsEnabled = true })];

            if (scope.Length == 0)
            {
                QuickStatus = "That tenant has no known subscriptions. Use Refresh available scope first.";
                return;
            }

            // Discovery only — the result is never written to the store, so nothing is remembered.
            var result = await _discovery.DiscoverAsync(profile, scope);

            var wantArc = QuickRouteKind == "Azure Arc";
            var name = QuickResourceName.Trim();
            var matches = result.Machines
                .Where(machine => wantArc
                    ? machine.Kind is MachineKind.ArcServer or MachineKind.AzureLocalVm
                    : machine.Kind is MachineKind.AzureVm)
                .Where(machine => name.Length == 0 ||
                                  machine.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(machine => machine.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            foreach (var machine in matches)
                QuickResults.Add(new MachineRow(machine, TenantLabel, SubscriptionLabel, null, null));

            QuickSelected = QuickResults.FirstOrDefault();
            QuickStatus = matches.Length == 0
                ? $"No {(wantArc ? "Arc" : "Azure")} machines matched in {scope.Length} subscription(s)."
                : $"{matches.Length} match(es) across {scope.Length} subscription(s). Pick one and connect.";
        });
    }

    [RelayCommand]
    private async Task QuickConnectSelectedAsync()
    {
        if (QuickSelected is not { } row) { QuickStatus = "Pick a machine first."; return; }
        if (row.SelectedPath is not { } path) { QuickStatus = "That machine has no usable route."; return; }
        if (_quickProfile is not { } profile) { QuickStatus = "Sign in first."; return; }

        var username = QuickUsername.Trim();
        var needsAccount = !row.Machine.SupportsEntraLogin;
        if (needsAccount && username.Length == 0)
        {
            QuickStatus = "That machine signs in with a local or domain account. Enter one.";
            QuickStep = 6;
            return;
        }

        IsQuickConnectOpen = false;
        await RunBusyAsync($"Connecting to {row.Name}", async () =>
        {
            // The typed account is held only long enough to launch. It is written to Windows
            // Credential Manager because that is the one path the launcher reads a password from,
            // and removed again immediately afterwards — nothing about this connection is kept.
            ConnectionIdentityProfile? account = null;
            var temporaryId = Guid.NewGuid();
            try
            {
                if (username.Length > 0)
                {
                    account = new ConnectionIdentityProfile(
                        temporaryId,
                        username,
                        username.Contains('\\') || username.Contains('@')
                            ? ConnectionIdentityKind.ActiveDirectory
                            : ConnectionIdentityKind.LocalAccount,
                        username,
                        _credentialStore.GetTargetName(temporaryId),
                        null);
                    await _credentialStore.SaveAsync(temporaryId, username, QuickPassword);
                }

                await _connections.ConnectAsync(row.Machine, path, profile, account, account);
                await ReloadSessionsAsync();
                await RecordAsync(
                    AuditCategory.Connection,
                    $"Quick connect to {row.Name}",
                    $"{DescribeRoute(path.Route)}; nothing saved to the estate");
                StatusMessage = $"{row.Name} — {DescribeRoute(path.Route)} started";
            }
            finally
            {
                QuickPassword = string.Empty;
                if (account is not null) await _credentialStore.DeleteAsync(temporaryId);
            }
        });
    }

    [RelayCommand]
    private void CancelQuickConnect()
    {
        IsQuickConnectOpen = false;
        QuickResults.Clear();
    }

    // ---------------------------------------------------------------- Lock, portability, activity

    public ObservableCollection<AuditRow> AuditEvents { get; } = [];

    /// <summary>
    /// How this installation is secured. Read-only: it is chosen once at first run and there is no
    /// passphrase to set here — the database is protected by Windows for the owning account.
    /// </summary>
    public string OwnerStatus => AppOwner.Current switch
    {
        { Kind: AppOwnerKind.EntraAccount } owner =>
            $"Owned by {owner.EntraUserPrincipalName}. Opening Stagecoach signs in to that Entra account.",
        { Kind: AppOwnerKind.WindowsAccount } owner =>
            $"Owned by {owner.DisplayName}. Opening Stagecoach verifies you with Windows Hello, " +
            "or your Windows password where Hello cannot prompt.",
        _ => "No owner is configured. Restart Stagecoach to run first-time setup.",
    };

    [ObservableProperty] private string _portablePath = string.Empty;

    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        await RunBusyAsync("Exporting settings", async () =>
        {
            var settings = new AppSettings(
                SelectedTheme, SelectedAccent, MinimizeToNotificationArea, SelectedCloseBehavior,
                BackgroundSyncEnabled, Math.Clamp(BackgroundSyncMinutes, 5, 1440), StartMinimized);
            var path = await PortableSettings.ExportAsync(_store, settings, PortableSettings.DefaultExportPath);
            PortablePath = path;
            await RecordAsync(AuditCategory.Identity, "Settings exported", Path.GetFileName(path));
            StatusMessage = $"Exported to {path}. It contains no passwords, tokens, or keys.";
        });
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        var path = PortablePath.Trim();
        if (path.Length == 0) { StatusMessage = "Enter the path of a settings export to import."; return; }

        await RunBusyAsync("Importing settings", async () =>
        {
            var (accounts, pins) = await PortableSettings.ImportAsync(_store, _settingsStore, path);
            await ReloadLocalAccountsAsync();
            await ReloadMachinesAsync();
            await RecordAsync(AuditCategory.Identity, "Settings imported",
                $"{accounts} local account(s), {pins} pinned machine(s)");
            StatusMessage =
                $"Imported {accounts} local account(s) and {pins} pin(s). Re-enter each account's password " +
                "and sign your Entra accounts in again.";
        });
    }

    /// <summary>
    /// Why the activity list is empty, or empty text when it is not. An empty list with no
    /// explanation is indistinguishable from a broken one, which is exactly how this looked.
    /// </summary>
    [ObservableProperty] private string _auditStatus = string.Empty;

    public bool HasNoAuditEvents => AuditEvents.Count == 0;

    [RelayCommand]
    private async Task RefreshAuditAsync()
    {
        try
        {
            var events = await _store.GetRecentAuditAsync(200);
            AuditEvents.Clear();
            foreach (var item in events) AuditEvents.Add(new AuditRow(item));
            AuditStatus = AuditEvents.Count == 0
                ? "Nothing recorded yet. Connecting an account or scanning will appear here."
                : string.Empty;
        }
        catch (Exception exception)
        {
            CrashLog.Record("Activity read", exception);
            AuditStatus = $"The activity log could not be read: {exception.Message}";
        }
        finally
        {
            OnPropertyChanged(nameof(HasNoAuditEvents));
        }
    }

    /// <summary>Appends one activity entry. Never called with a credential, token, or resource id.</summary>
    private async Task RecordAsync(AuditCategory category, string summary, string? detail = null)
    {
        try
        {
            await _store.AppendAuditAsync(new AuditEvent(Guid.NewGuid(), DateTimeOffset.Now, category, summary, detail));
            await RefreshAuditAsync();
        }
        catch (Exception exception)
        {
            // Never take the caller down over a log entry, but never hide it either: a swallowed
            // failure here is what made the Activity page look empty instead of broken.
            CrashLog.Record("Audit append", exception);
            AuditStatus = $"An activity entry could not be saved: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateSupportBundleAsync()
    {
        await RunBusyAsync("Collecting support information", async () =>
        {
            var path = await SupportBundle.CreateAsync(CurrentVersion, _lastReadiness, StatusMessage);
            LatestSupportBundlePath = path;
            SupportBundleStatus = $"Saved {Path.GetFileName(path)}. Attach this file to your support request.";
            StatusMessage = SupportBundleStatus;
        });
    }

    [RelayCommand]
    private void OpenSupportFolder()
    {
        try
        {
            Directory.CreateDirectory(SupportBundle.Directory);
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SupportBundle.Directory,
                    UseShellExecute = true,
                },
            };
            process.Start();
        }
        catch (Exception exception)
        {
            CrashLog.Record("Open support folder", exception);
            StatusMessage = SafeMessage(exception);
        }
    }

    [RelayCommand]
    private async Task PrepareWorkstationAsync()
    {
        // Takes the cancellation token: this is the operation that hung for eight hours, and it must
        // be stoppable from the window rather than only by killing the application.
        await RunBusyAsync("Installing or updating local Azure CLI extensions", async cancellationToken =>
        {
            await _readiness.PrepareCliExtensionsAsync(cancellationToken);
            var result = await _readiness.InspectAsync(cancellationToken);
            _lastReadiness = result;
            WorkstationStatus = result.Actions.Count == 0 ? "Workstation ready" : string.Join("  •  ", result.Actions);
            StatusMessage = "Local Azure CLI prerequisites are ready.";
        });
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        // A failed check used to leave "Updates have not been checked" on screen, so the button
        // looked like it did nothing at all. The panel now always reports the outcome.
        IsUpdateReadyToInstall = false;
        _verifiedUpdate = null;
        AvailableUpdate = null;
        UpdateStatus = "Checking…";
        try
        {
            IsBusy = true;
            StatusMessage = "Checking for a new Stagecoach release";
            var release = await _updates.CheckAsync();
            AvailableUpdate = release;
            UpdateStatus = release.Availability switch
            {
                ReleaseUpdateAvailability.Available =>
                    $"Stagecoach {release.LatestVersion} is available. You are on {release.CurrentVersion}.",
                ReleaseUpdateAvailability.Current =>
                    $"Stagecoach {release.CurrentVersion} is the current release.",
                ReleaseUpdateAvailability.DevelopmentBuild =>
                    $"This is a development build ({release.CurrentVersion}), so it will not update itself. " +
                    $"The current published release is {release.LatestVersion}.",
                _ => "Update state is unknown.",
            };
            StatusMessage = UpdateStatus;
        }
        catch (Exception exception)
        {
            CrashLog.Record("Update check", exception);
            UpdateStatus = $"Update check failed. {SafeMessage(exception)}";
            StatusMessage = UpdateStatus;
            RaiseError("Could not check for updates", UpdateStatus);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (AvailableUpdate is not { Availability: ReleaseUpdateAvailability.Available } release)
        {
            StatusMessage = "Check for updates first.";
            return;
        }

        await RunBusyAsync($"Downloading Stagecoach {release.LatestVersion}", async () =>
        {
            _verifiedUpdate = await _updates.DownloadAndVerifyAsync(release);
            IsUpdateReadyToInstall = true;
            UpdateStatus = $"Stagecoach {release.LatestVersion} downloaded and SHA-256 verified.";
            StatusMessage = UpdateStatus;
        });
    }

    /// <summary>
    /// Raised once Windows Installer has actually started and elevation was granted, so the shell
    /// can close Stagecoach. Windows cannot replace a running executable in place, so staying open
    /// means the upgrade fails on locked files or demands a reboot.
    /// </summary>
    public event Action? ShutdownForUpdateRequested;

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_verifiedUpdate is not { } update) { StatusMessage = "Download the update first."; return; }

        // Installing closes Stagecoach, which tears down every helper process it owns.
        if (ActiveSessionCount > 0 && !_updateWillCloseSessionsAcknowledged)
        {
            _updateWillCloseSessionsAcknowledged = true;
            UpdateStatus =
                $"Installing will close Stagecoach and end {ActiveSessionCount} running session(s). " +
                "Choose Install update again to continue.";
            StatusMessage = UpdateStatus;
            RaiseError("Sessions are still running", UpdateStatus);
            return;
        }

        await RunBusyAsync("Starting Windows Installer", async () =>
        {
            // Only returns once the elevation prompt was accepted; a cancelled prompt throws, so
            // Stagecoach stays open rather than closing for an install that never began.
            await _updates.LaunchAsync(update);
            UpdateStatus = "Windows Installer is running. Stagecoach is closing so it can be replaced.";
            StatusMessage = UpdateStatus;
            ShutdownForUpdateRequested?.Invoke();
        });
    }

    private bool _updateWillCloseSessionsAcknowledged;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(new AppSettings(
            SelectedTheme, SelectedAccent, MinimizeToNotificationArea, SelectedCloseBehavior,
            BackgroundSyncEnabled, Math.Clamp(BackgroundSyncMinutes, 5, 1440), StartMinimized));
        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private void DismissError()
    {
        HasActionableError = false;
        ErrorTitle = ErrorMessage = string.Empty;
    }

    // ---------------------------------------------------------------- Reload and filtering

    partial void OnSearchTextChanged(string value) => ApplyMachineFilter();
    partial void OnFavoritesOnlyChanged(bool value) => ApplyMachineFilter();
    partial void OnReadyOnlyChanged(bool value) => ApplyMachineFilter();
    partial void OnPinnedOnlyChanged(bool value) => ApplyMachineFilter();
    partial void OnSelectedTenantFilterChanged(FilterOption? value) => ApplyMachineFilter();
    partial void OnSelectedSubscriptionFilterChanged(FilterOption? value) => ApplyMachineFilter();
    partial void OnSelectedSourceFilterChanged(FilterOption? value) => ApplyMachineFilter();
    partial void OnSelectedOperatingSystemFilterChanged(FilterOption? value) => ApplyMachineFilter();
    partial void OnSelectedStateFilterChanged(FilterOption? value) => ApplyMachineFilter();

    partial void OnSelectedIdentityChanged(IdentityRow? value)
    {
        OnPropertyChanged(nameof(ActiveIdentityContext));
        RenameIdentityText = value?.DisplayName ?? string.Empty;
        if (value is null) return;

        // Fire-and-forget, so it must never throw into an unobserved task: an escaped exception
        // here surfaces as a raw crash rather than the error banner.
        _ = LoadScopeSafelyAsync(value.Profile.Id);
    }

    private async Task LoadScopeSafelyAsync(Guid identityId)
    {
        try
        {
            await LoadScopeAsync(identityId);
        }
        catch (Exception exception)
        {
            StatusMessage = SafeMessage(exception);
            RaiseError("Could not load tenants and subscriptions", StatusMessage);
        }
    }

    private async Task ReloadIdentitiesAsync()
    {
        var selectedId = SelectedIdentity?.Profile.Id;
        Identities.Clear();
        foreach (var identity in await _store.GetIdentitiesAsync()) Identities.Add(new IdentityRow(identity));
        SelectedIdentity = Identities.FirstOrDefault(item => item.Profile.Id == selectedId) ?? Identities.FirstOrDefault();

        _tenantNames.Clear();
        _subscriptionNames.Clear();
        foreach (var identity in Identities)
        {
            foreach (var tenant in await _store.GetTenantsAsync(identity.Profile.Id))
                _tenantNames[tenant.TenantId] = tenant.DisplayName;
            foreach (var subscription in await _store.GetSubscriptionsAsync(identity.Profile.Id))
                _subscriptionNames[subscription.SubscriptionId] = subscription.DisplayName;
        }

        OnPropertyChanged(nameof(HasIdentities));
        OnPropertyChanged(nameof(ActiveIdentityContext));
        RefreshSetupState();
    }

    private async Task LoadScopeAsync(Guid identityId)
    {
        var tenants = await _store.GetTenantsAsync(identityId);
        var includedTenants = tenants
            .Where(item => item.IsEnabled)
            .Select(item => item.TenantId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Tenants.Clear();
        foreach (var item in tenants) Tenants.Add(new TenantRow(item));
        Subscriptions.Clear();
        foreach (var item in await _store.GetSubscriptionsAsync(identityId))
            Subscriptions.Add(new SubscriptionRow(item, includedTenants.Contains(item.TenantId)));
        RefreshSetupState();
    }

    /// <summary>
    /// Subscriptions that would actually be scanned: enabled, and under an enabled tenant.
    /// Excluding a tenant has to exclude everything beneath it, otherwise a subscription left
    /// enabled under an excluded tenant would still be queried.
    /// </summary>
    private async Task<IReadOnlyList<SubscriptionScope>> GetScannableSubscriptionsAsync(Guid identityId)
    {
        var includedTenants = (await _store.GetTenantsAsync(identityId))
            .Where(item => item.IsEnabled)
            .Select(item => item.TenantId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (await _store.GetSubscriptionsAsync(identityId))
            .Where(item => includedTenants.Contains(item.TenantId))
            .ToArray();
    }

    private async Task ReloadMachinesAsync()
    {
        _allMachines.Clear();
        _allMachines.AddRange(await _store.GetMachinesAsync());
        _pins.Clear();
        foreach (var pair in await _store.GetMachinePinsAsync()) _pins[pair.Key] = pair.Value;
        RebuildFilterOptions();
        ApplyMachineFilter();
        OnPropertyChanged(nameof(HasMachines));
        RefreshSetupState();
    }

    private void ResetFilterOptions()
    {
        TenantFilters.Clear();
        TenantFilters.Add(FilterOption.All("All tenants"));
        SubscriptionFilters.Clear();
        SubscriptionFilters.Add(FilterOption.All("All subscriptions"));
        SourceFilters.Clear();
        SourceFilters.Add(FilterOption.All("All sources"));
        OperatingSystemFilters.Clear();
        OperatingSystemFilters.Add(FilterOption.All("Any OS"));
        StateFilters.Clear();
        StateFilters.Add(FilterOption.All("Any state"));
        SelectedTenantFilter = TenantFilters[0];
        SelectedSubscriptionFilter = SubscriptionFilters[0];
        SelectedSourceFilter = SourceFilters[0];
        SelectedOperatingSystemFilter = OperatingSystemFilters[0];
        SelectedStateFilter = StateFilters[0];
    }

    private void RebuildFilterOptions()
    {
        var tenant = SelectedTenantFilter?.Value;
        var subscription = SelectedSubscriptionFilter?.Value;
        var source = SelectedSourceFilter?.Value;
        var operatingSystem = SelectedOperatingSystemFilter?.Value;
        var state = SelectedStateFilter?.Value;

        Fill(TenantFilters, "All tenants", _allMachines
            .SelectMany(machine => machine.AccessPaths.Select(path => path.TenantId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new FilterOption(TenantLabel(id), id)));
        Fill(SubscriptionFilters, "All subscriptions", _allMachines
            .SelectMany(machine => machine.AccessPaths.Select(path => path.SubscriptionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new FilterOption(SubscriptionLabel(id), id)));
        Fill(SourceFilters, "All sources", _allMachines
            .Select(machine => MachineRow.DescribeSource(machine.Kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new FilterOption(value, value)));
        Fill(OperatingSystemFilters, "Any OS", _allMachines
            .Select(machine => machine.OperatingSystem.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new FilterOption(value, value)));
        Fill(StateFilters, "Any state", _allMachines
            .Select(MachineRow.DescribeState)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new FilterOption(value, value)));

        SelectedTenantFilter = Restore(TenantFilters, tenant);
        SelectedSubscriptionFilter = Restore(SubscriptionFilters, subscription);
        SelectedSourceFilter = Restore(SourceFilters, source);
        SelectedOperatingSystemFilter = Restore(OperatingSystemFilters, operatingSystem);
        SelectedStateFilter = Restore(StateFilters, state);

        static void Fill(ObservableCollection<FilterOption> target, string allLabel, IEnumerable<FilterOption> options)
        {
            target.Clear();
            target.Add(FilterOption.All(allLabel));
            foreach (var option in options.OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase))
                target.Add(option);
        }

        static FilterOption Restore(ObservableCollection<FilterOption> source, string? value) =>
            source.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))
            ?? source[0];
    }

    private string TenantLabel(string tenantId) =>
        _tenantNames.TryGetValue(tenantId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : tenantId;

    private string SubscriptionLabel(string subscriptionId) =>
        _subscriptionNames.TryGetValue(subscriptionId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : subscriptionId;

    private void ApplyMachineFilter()
    {
        var query = SearchText.Trim();
        var tenant = SelectedTenantFilter?.Value;
        var subscription = SelectedSubscriptionFilter?.Value;
        var source = SelectedSourceFilter?.Value;
        var operatingSystem = SelectedOperatingSystemFilter?.Value;
        var state = SelectedStateFilter?.Value;

        var filtered = _allMachines.Where(machine =>
            (string.IsNullOrWhiteSpace(query) ||
             machine.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             machine.ResourceGroup.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             (machine.DomainName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
             machine.AccessPaths.Any(path =>
                 SubscriptionLabel(path.SubscriptionId).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 TenantLabel(path.TenantId).Contains(query, StringComparison.OrdinalIgnoreCase))) &&
            (tenant is null || machine.AccessPaths.Any(path =>
                string.Equals(path.TenantId, tenant, StringComparison.OrdinalIgnoreCase))) &&
            (subscription is null || machine.AccessPaths.Any(path =>
                string.Equals(path.SubscriptionId, subscription, StringComparison.OrdinalIgnoreCase))) &&
            (source is null || string.Equals(MachineRow.DescribeSource(machine.Kind), source, StringComparison.OrdinalIgnoreCase)) &&
            (operatingSystem is null || string.Equals(machine.OperatingSystem.ToString(), operatingSystem, StringComparison.OrdinalIgnoreCase)) &&
            (state is null || string.Equals(MachineRow.DescribeState(machine), state, StringComparison.OrdinalIgnoreCase)) &&
            (!FavoritesOnly || machine.IsFavorite) &&
            (!ReadyOnly || machine.BestReadiness == ReadinessState.Ready) &&
            (!PinnedOnly || _pins.ContainsKey(machine.StableKey)));

        var accountNames = LocalAccounts.ToDictionary(item => item.Profile.Id, item => item.DisplayName);
        Machines.Clear();
        foreach (var machine in filtered
                     .OrderByDescending(item => item.IsFavorite)
                     .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Guid? pinnedId = _pins.TryGetValue(machine.StableKey, out var value) ? value : null;
            var pinnedName = pinnedId is { } id ? accountNames.GetValueOrDefault(id) : null;
            Machines.Add(new MachineRow(machine, TenantLabel, SubscriptionLabel, pinnedId, pinnedName));
        }

        OnPropertyChanged(nameof(HasMachines));
        OnPropertyChanged(nameof(EstateSummary));
    }

    private async Task ReloadLocalAccountsAsync()
    {
        LocalAccounts.Clear();
        foreach (var profile in await _store.GetConnectionIdentitiesAsync())
            LocalAccounts.Add(new LocalAccountRow(profile));
        OnPropertyChanged(nameof(HasLocalAccounts));
    }

    private async Task ReloadSessionsAsync()
    {
        Sessions.Clear();
        foreach (var session in await _connections.GetSessionsAsync()) Sessions.Add(new SessionRow(session));
        OnPropertyChanged(nameof(ActiveSessionCount));
    }

    private static string DescribeRoute(ConnectionRouteKind route) => route switch
    {
        ConnectionRouteKind.DirectRdp => "direct RDP",
        ConnectionRouteKind.DirectSsh => "direct SSH",
        ConnectionRouteKind.BastionRdp => "Bastion RDP",
        ConnectionRouteKind.BastionTunnelRdp => "Bastion tunnel RDP",
        ConnectionRouteKind.BastionSsh => "Bastion SSH",
        ConnectionRouteKind.ArcRdp => "Arc RDP",
        _ => "Arc SSH",
    };

    private void RaiseError(string title, string message)
    {
        ErrorTitle = title;
        ErrorMessage = message;
        HasActionableError = true;
    }

    private Task RunBusyAsync(string message, Func<Task> operation) =>
        RunBusyAsync(message, _ => operation());

    /// <summary>
    /// Runs one operation at a time, and says so when something is already running.
    /// <para>
    /// This used to be <c>if (IsBusy) return;</c> — every other command silently did nothing while
    /// one was in flight. With an Azure CLI call that could never time out, an operator sat for eight
    /// hours on "Installing or updating local Azure CLI extensions" while every button, the activity
    /// list and the support bundle quietly refused to work and said nothing at all.
    /// </para>
    /// </summary>
    private async Task RunBusyAsync(string message, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            StatusMessage =
                $"Still working on \"{BusyMessage}\". Wait for it to finish, or cancel it, then try again.";
            RaiseError("Something is already running", StatusMessage);
            return;
        }

        IsBusy = true;
        BusyMessage = message;
        StatusMessage = message;
        using var cancellation = new CancellationTokenSource();
        _busyCancellation = cancellation;
        OnPropertyChanged(nameof(CanCancelBusy));
        try
        {
            await operation(cancellation.Token);
        }
        catch (Exception exception)
        {
            CrashLog.Record(message, exception);
            StatusMessage = SafeMessage(exception);
            RaiseError("That did not complete", StatusMessage);
        }
        finally
        {
            _busyCancellation = null;
            BusyMessage = string.Empty;
            IsBusy = false;
            OnPropertyChanged(nameof(CanCancelBusy));
        }
    }

    private CancellationTokenSource? _busyCancellation;

    /// <summary>What is running right now, so a blocked action can name it instead of doing nothing.</summary>
    [ObservableProperty] private string _busyMessage = string.Empty;

    public bool CanCancelBusy => _busyCancellation is not null;

    /// <summary>Stops whatever is running. Nothing may hold the application indefinitely.</summary>
    [RelayCommand]
    private void CancelBusy()
    {
        if (_busyCancellation is not { } cancellation) return;
        StatusMessage = $"Stopping \"{BusyMessage}\"…";
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    internal static string SafeMessage(Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException:
                return "Operation cancelled.";
            case InvalidOperationException:
            case InvalidDataException:
                return exception.Message;
            case HttpRequestException:
                return "Stagecoach could not reach the release service.";
            case UnauthorizedAccessException:
                return $"Windows denied access: {exception.Message}";
        }

        // Storage failures used to collapse into "unexpected local error", which told an operator
        // nothing and hid the one message that actually identifies the problem. Matched by name so
        // the view model keeps no dependency on the SQLite package.
        if (exception.GetType().Name is "SqliteException")
        {
            var detail = exception.Message;
            if (detail.Contains("readonly", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("read-only", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("unable to open", StringComparison.OrdinalIgnoreCase))
                return "Stagecoach could not write its local database. Its folder is most likely " +
                       "redirected to OneDrive or a network share, blocked by controlled folder " +
                       "access or antivirus, or owned by a different account. " +
                       $"Folder: {Infrastructure.StagecoachPaths.RootDirectory}. Detail: {detail}";
            return $"Stagecoach could not read or write its local database. {detail}";
        }

        return $"Stagecoach hit an unexpected local error ({exception.GetType().Name}): {exception.Message}";
    }
}

public sealed record FilterOption(string Label, string? Value)
{
    public static FilterOption All(string label) => new(label, null);
    public override string ToString() => Label;
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

/// <summary>
/// A subscription and whether its tenant is included. Excluding a tenant excludes everything under
/// it, so a subscription there is shown greyed out and cannot be toggled — leaving it looking
/// available implied it would be scanned when it never would be.
/// </summary>
public sealed record SubscriptionRow(SubscriptionScope Scope, bool IsTenantIncluded = true)
{
    public string SubscriptionId => Scope.SubscriptionId;
    public string DisplayName => Scope.DisplayName;
    public string State => Scope.State;
    public bool IsEnabled => Scope.IsEnabled;

    /// <summary>True only when this subscription would actually be scanned.</summary>
    public bool IsEffectivelyIncluded => IsTenantIncluded && IsEnabled;

    public bool CanToggle => IsTenantIncluded;
    public double RowOpacity => IsTenantIncluded ? 1.0 : 0.45;

    public string Selection => !IsTenantIncluded
        ? "Tenant excluded"
        : IsEnabled ? "Included" : Scope.RequiresReview ? "Review" : "Excluded";
}

public partial class MachineRow : ObservableObject
{
    public MachineRow(
        MachineRecord machine,
        Func<string, string> tenantLabel,
        Func<string, string> subscriptionLabel,
        Guid? pinnedAccountId,
        string? pinnedAccountName)
    {
        Machine = machine;
        Paths = machine.AccessPaths.Select(path => new AccessPathRow(path)).ToArray();
        SelectedRoute = Paths.FirstOrDefault(item => item.Path.IsPreferred) ?? Paths.FirstOrDefault();
        var preferred = machine.AccessPaths.FirstOrDefault(path => path.IsPreferred) ?? machine.AccessPaths.FirstOrDefault();
        Tenant = preferred is null ? "—" : tenantLabel(preferred.TenantId);
        Subscription = preferred is null ? "—" : subscriptionLabel(preferred.SubscriptionId);
        PinnedAccountId = pinnedAccountId;
        PinnedAccountName = pinnedAccountName;
    }

    public MachineRecord Machine { get; }
    public IReadOnlyList<AccessPathRow> Paths { get; }
    public string Tenant { get; }
    public string Subscription { get; }
    public Guid? PinnedAccountId { get; private set; }
    public string? PinnedAccountName { get; private set; }

    [ObservableProperty] private AccessPathRow? _selectedRoute;

    public AzureAccessPath? SelectedPath => SelectedRoute?.Path;

    partial void OnSelectedRouteChanged(AccessPathRow? value)
    {
        OnPropertyChanged(nameof(SelectedPath));
        OnPropertyChanged(nameof(Route));
        OnPropertyChanged(nameof(Readiness));
    }

    public void ApplyPin(Guid accountId, string accountName)
    {
        PinnedAccountId = accountId;
        PinnedAccountName = accountName;
        OnPropertyChanged(nameof(PinnedAccountId));
        OnPropertyChanged(nameof(PinnedAccountName));
        OnPropertyChanged(nameof(Account));
    }

    public string Name => Machine.Name;
    public string Source => DescribeSource(Machine.Kind);
    public string OperatingSystem => Machine.OperatingSystem.ToString();
    public string State => DescribeState(Machine);
    public string Route => SelectedPath is null ? "None" : DescribeRouteShort(SelectedPath.Route);
    public string Readiness => (SelectedPath?.Readiness ?? Machine.BestReadiness) switch
    {
        ReadinessState.Ready => "Ready",
        ReadinessState.InteractionRequired => "Sign-in",
        ReadinessState.MissingPrerequisite => "Prereq",
        ReadinessState.Offline => "Offline",
        ReadinessState.PermissionDenied => "Denied",
        ReadinessState.Unsupported => "Unsupported",
        _ => "Unknown",
    };
    /// <summary>
    /// What will sign in to the guest. A machine carrying the Entra login extension needs no local
    /// account at all; anything else needs one pinned, and saying "Ask" for a machine that does not
    /// need an account was misleading.
    /// </summary>
    public string Account => PinnedAccountName ?? (Machine.SupportsEntraLogin ? "Entra sign-in" : "Ask");

    public string SignInKind => Machine.SupportsEntraLogin
        ? "Microsoft Entra — your work account signs in to the machine"
        : "Local or domain account — a local account is needed";
    public string Favorite => Machine.IsFavorite ? "★" : "☆";
    public string ReasonText => SelectedPath?.Reason ?? "No route was correlated for this machine.";
    public bool IsReady => (SelectedPath?.Readiness ?? Machine.BestReadiness) == ReadinessState.Ready;

    public static string DescribeSource(MachineKind kind) => kind switch
    {
        MachineKind.AzureVm => "Azure",
        MachineKind.ArcServer => "Arc",
        _ => "Azure Local",
    };

    public static string DescribeState(MachineRecord machine) =>
        string.IsNullOrWhiteSpace(machine.AgentState) ? machine.PowerState : machine.AgentState;

    private static string DescribeRouteShort(ConnectionRouteKind route) => route switch
    {
        ConnectionRouteKind.DirectRdp => "Direct RDP",
        ConnectionRouteKind.DirectSsh => "Direct SSH",
        ConnectionRouteKind.BastionRdp => "Bastion RDP",
        ConnectionRouteKind.BastionTunnelRdp => "Bastion tunnel",
        ConnectionRouteKind.BastionSsh => "Bastion SSH",
        ConnectionRouteKind.ArcRdp => "Arc RDP",
        _ => "Arc SSH",
    };
}

public sealed record AccessPathRow(AzureAccessPath Path)
{
    public override string ToString() => $"{Path.Route} — {Path.Readiness}";
}

public sealed record LocalAccountRow(ConnectionIdentityProfile Profile)
{
    public string DisplayName => Profile.DisplayName;
    public string Username => Profile.Username;
    public string Kind => Profile.Username.Contains('\\') || Profile.Username.Contains('@')
        ? "Domain account"
        : "Local account";
    public string Credential => Profile.CredentialTarget is null
        ? "No stored password"
        : "Password in Windows Credential Manager";
    public override string ToString() => $"{DisplayName} ({Username})";
}

public sealed record SessionRow(ConnectionSession Session)
{
    public string Machine => Session.MachineName;
    public string Route => Session.Route.ToString();
    public string State => Session.State.ToString();
    public string Started => Session.StartedAt.LocalDateTime.ToString("g");
    public string Detail => Session.SafeStatus ?? string.Empty;
}

public sealed record AuditRow(AuditEvent Event)
{
    public string When => Event.OccurredAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string Category => Event.Category.ToString();
    public string Summary => Event.Summary;
    public string Detail => Event.Detail ?? string.Empty;
}
