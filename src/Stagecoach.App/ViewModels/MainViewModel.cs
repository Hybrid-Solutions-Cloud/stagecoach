using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagecoach.Core.Interfaces;
using Stagecoach.Core.Models;

namespace Stagecoach.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IMetadataStore _store;
    private readonly IDiscoveryService _discovery;
    private readonly ICredentialResolver _credentialResolver;
    private readonly IProcessOrchestrator _orchestrator;

    [ObservableProperty]
    private string _currentTab = "Estate";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedDomainFilter = "ALL";

    [ObservableProperty]
    private StagecoachMachine? _selectedMachine;

    [ObservableProperty]
    private string _targetUsername = string.Empty;

    [ObservableProperty]
    private string _targetPassword = string.Empty;

    [ObservableProperty]
    private bool _saveToKeyVault;

    [ObservableProperty]
    private string _credentialStatusText = string.Empty;

    [ObservableProperty]
    private bool _isDrawerOpen;

    public ObservableCollection<StagecoachMachine> Machines { get; } = new();
    public ObservableCollection<StagecoachMachine> FilteredMachines { get; } = new();
    public ObservableCollection<StagecoachIdentity> Identities { get; } = new();
    public ObservableCollection<StagecoachMachine> RecentMachines { get; } = new();
    public ObservableCollection<StagecoachSession> ActiveSessions { get; } = new();

    public MainViewModel(IMetadataStore store, IDiscoveryService discovery, ICredentialResolver credentialResolver, IProcessOrchestrator orchestrator)
    {
        _store = store;
        _discovery = discovery;
        _credentialResolver = credentialResolver;
        _orchestrator = orchestrator;
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusMessage = "Loading local estate database...";
        try
        {
            await _store.InitializeAsync();
            var cached = await _store.GetAllMachinesAsync();
            Machines.Clear();
            foreach (var m in cached) Machines.Add(m);
            ApplyFilters();

            var recents = await _store.GetRecentMachinesAsync();
            RecentMachines.Clear();
            foreach (var r in recents) RecentMachines.Add(r);

            var identities = await _discovery.GetIdentitiesAsync();
            Identities.Clear();
            foreach (var id in identities) Identities.Add(id);

            StatusMessage = $"Ready. {Machines.Count} machines loaded from local store.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Init warning: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void SelectTab(string tab)
    {
        CurrentTab = tab;
    }

    [RelayCommand]
    public async Task SyncEstateAsync()
    {
        IsBusy = true;
        StatusMessage = "Syncing estate across all accessible Azure tenants...";
        try
        {
            var discovered = await _discovery.DiscoverEstateAsync();
            await _store.SaveMachinesAsync(discovered);

            var all = await _store.GetAllMachinesAsync();
            Machines.Clear();
            foreach (var m in all) Machines.Add(m);
            ApplyFilters();

            var idList = await _discovery.GetIdentitiesAsync();
            Identities.Clear();
            foreach (var id in idList) Identities.Add(id);

            StatusMessage = $"Sync complete! Discovered {discovered.Count} machines across tenants.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddAccountAsync()
    {
        IsBusy = true;
        StatusMessage = "Opening Microsoft sign-in...";
        try
        {
            var ok = await _discovery.TriggerInteractiveLoginAsync();
            if (ok)
            {
                var idList = await _discovery.GetIdentitiesAsync();
                Identities.Clear();
                foreach (var id in idList) Identities.Add(id);
                StatusMessage = "Account connected! Click 'Sync Estate' to discover machines.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sign in error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(StagecoachMachine? machine)
    {
        if (machine == null) return;
        machine.IsFavorite = !machine.IsFavorite;
        await _store.SetFavoriteAsync(machine.Id, machine.IsFavorite);
        ApplyFilters();
    }

    [RelayCommand]
    public async Task OpenConnectDrawerAsync(StagecoachMachine? machine)
    {
        if (machine == null) return;
        SelectedMachine = machine;
        TargetPassword = string.Empty;
        SaveToKeyVault = false;
        IsDrawerOpen = true;

        TargetUsername = machine.DomainType == DomainType.ActiveDirectory
            ? (!string.IsNullOrWhiteSpace(machine.DomainName) ? $"{machine.DomainName}\\Administrator" : "CORP\\Administrator")
            : ".\\Administrator";

        CredentialStatusText = "Checking LAPS / Key Vault...";
        var cred = await _credentialResolver.ResolveCredentialAsync(machine);
        if (cred.IsResolved)
        {
            CredentialStatusText = $"✓ Resolved via {cred.Source}";
            if (!string.IsNullOrWhiteSpace(cred.Username)) TargetUsername = cred.Username;
        }
        else
        {
            CredentialStatusText = "ℹ Standard authentication / Operator prompt";
        }
    }

    [RelayCommand]
    public void CloseDrawer()
    {
        IsDrawerOpen = false;
        SelectedMachine = null;
    }

    [RelayCommand]
    public async Task LaunchConnectionAsync()
    {
        if (SelectedMachine == null) return;
        IsBusy = true;
        StatusMessage = $"Launching remote session for {SelectedMachine.Name}...";

        if (SaveToKeyVault && !string.IsNullOrWhiteSpace(TargetPassword))
        {
            await _credentialResolver.SaveWorkgroupSecretAsync(SelectedMachine.Name, TargetPassword);
        }

        try
        {
            var session = await _orchestrator.ConnectAsync(SelectedMachine, TargetUsername, TargetPassword);
            await _store.RecordConnectionAsync(SelectedMachine.Id);

            ActiveSessions.Add(session);
            StatusMessage = $"Remote Desktop (MSTSC) launched for {SelectedMachine.Name}!";
            IsDrawerOpen = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Launch error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedDomainFilterChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var query = Machines.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(m => m.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                     m.DomainName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                     m.ResourceGroup.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedDomainFilter != "ALL")
        {
            if (Enum.TryParse<DomainType>(SelectedDomainFilter, out var dt))
            {
                query = query.Where(m => m.DomainType == dt);
            }
        }

        FilteredMachines.Clear();
        foreach (var m in query.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.Name))
        {
            FilteredMachines.Add(m);
        }
    }
}
