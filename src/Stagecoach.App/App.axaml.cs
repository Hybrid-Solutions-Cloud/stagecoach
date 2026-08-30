using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Stagecoach.App.ViewModels;
using Stagecoach.App.Views;
using Stagecoach.Core;
using Stagecoach.Infrastructure;
using Stagecoach.Infrastructure.Azure;
using Stagecoach.Infrastructure.Orchestration;
using Stagecoach.Infrastructure.Readiness;
using Stagecoach.Infrastructure.Remediation;
using Stagecoach.Infrastructure.Security;
using Stagecoach.Infrastructure.Storage;

namespace Stagecoach.App;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private DispatcherTimer? _syncTimer;
    private MainViewModel? _syncViewModel;
    private WindowIcon? _icon;
    private bool _allowExit;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            StagecoachPaths.EnsureDirectories();
            var store = new EncryptedSqliteMetadataStore();
            var cli = new AzureCliRunner();
            var credentials = new WindowsCredentialManager();
            var identityService = new AzureCliIdentityService(cli, store);
            var discovery = new ResourceGraphDiscoveryService(cli);
            var readiness = new WorkstationReadinessService(cli);
            var remediation = new ArcRemediationService(cli);
            var connections = new ProcessOrchestrator(cli, credentials, store);
            var settingsStore = new AppSettingsStore(Path.Combine(StagecoachPaths.RootDirectory, "settings.json"));
            var viewModel = new MainViewModel(store, identityService, discovery, credentials, connections, readiness, remediation, settingsStore);
            _icon = StagecoachIcon.Create();
            var window = new MainWindow { DataContext = viewModel, Icon = _icon };
            desktop.MainWindow = window;
            ConfigureTray(desktop, window, viewModel);
            window.Opened += async (_, _) =>
            {
                await viewModel.InitializeAsync();
                ApplyTheme(viewModel.SelectedTheme);
                ApplyAccent(viewModel.SelectedAccent);
                ConfigureBackgroundSync(viewModel);
                if (viewModel.StartMinimized) HideToTray(window);
            };
            window.Closing += (_, args) =>
            {
                if (_allowExit) return;
                args.Cancel = true;
                if (viewModel.SelectedCloseBehavior == CloseBehavior.Exit) Exit(desktop);
                else HideToTray(window);
            };
            window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Window.WindowStateProperty &&
                    window.WindowState == WindowState.Minimized && viewModel.MinimizeToNotificationArea)
                    HideToTray(window);
            };
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedTheme)) ApplyTheme(viewModel.SelectedTheme);
                if (args.PropertyName == nameof(MainViewModel.SelectedAccent)) ApplyAccent(viewModel.SelectedAccent);
                if (args.PropertyName is nameof(MainViewModel.BackgroundSyncEnabled) or nameof(MainViewModel.BackgroundSyncMinutes))
                    ConfigureBackgroundSync(viewModel);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureTray(IClassicDesktopStyleApplicationLifetime desktop, Window window, MainViewModel viewModel)
    {
        var show = new NativeMenuItem("Show Stagecoach");
        var sync = new NativeMenuItem("Sync estate");
        var exit = new NativeMenuItem("Exit");
        var menu = new NativeMenu();
        menu.Add(show); menu.Add(sync); menu.Add(new NativeMenuItemSeparator()); menu.Add(exit);
        _trayIcon = new TrayIcon { ToolTipText = "Stagecoach — starting", Menu = menu, Icon = _icon, IsVisible = true };
        TrayIcon.SetIcons(this, [_trayIcon]);
        void Show()
        {
            window.ShowInTaskbar = true;
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
        show.Click += (_, _) => Show();
        _trayIcon.Clicked += (_, _) => Show();
        sync.Click += async (_, _) => await viewModel.SyncEstateAsync();
        exit.Click += (_, _) => Exit(desktop);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.StatusMessage) or nameof(MainViewModel.IsBusy))
                _trayIcon.ToolTipText = viewModel.IsBusy ? "Stagecoach — working" : $"Stagecoach — {viewModel.StatusMessage}";
        };
    }

    private static void HideToTray(Window window)
    {
        window.ShowInTaskbar = false;
        window.Hide();
    }

    private void Exit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _allowExit = true;
        _syncTimer?.Stop();
        if (_trayIcon is not null) { _trayIcon.IsVisible = false; _trayIcon.Dispose(); }
        desktop.Shutdown();
    }

    private void ConfigureBackgroundSync(MainViewModel viewModel)
    {
        _syncTimer?.Stop();
        _syncTimer ??= new DispatcherTimer();
        _syncTimer.Tick -= OnBackgroundSyncTick;
        _syncViewModel = viewModel;
        if (!viewModel.BackgroundSyncEnabled) return;
        _syncTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(viewModel.BackgroundSyncMinutes, 5, 1440));
        _syncTimer.Tick += OnBackgroundSyncTick;
        _syncTimer.Start();
    }

    private async void OnBackgroundSyncTick(object? sender, EventArgs args)
    {
        if (_syncViewModel is { IsBusy: false } viewModel) await viewModel.SyncEstateAsync();
    }

    private void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private void ApplyAccent(AppAccent accent) => Resources["StagecoachAccentBrush"] = new SolidColorBrush(accent switch
    {
        AppAccent.Blue => Color.Parse("#2563A6"),
        AppAccent.Green => Color.Parse("#2D7D5B"),
        AppAccent.Purple => Color.Parse("#7651A8"),
        _ => Color.Parse("#B9552D"),
    });
}
