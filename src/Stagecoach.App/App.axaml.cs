using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Stagecoach.App.Security;
using Stagecoach.App.ViewModels;
using Stagecoach.App.Views;
using Stagecoach.Infrastructure;
using Stagecoach.Infrastructure.Azure;
using Stagecoach.Infrastructure.Orchestration;
using Stagecoach.Infrastructure.Readiness;
using Stagecoach.Infrastructure.Remediation;
using Stagecoach.Infrastructure.Security;
using Stagecoach.Infrastructure.Storage;
using Stagecoach.Infrastructure.Updates;
using Stagecoach.Core;

namespace Stagecoach.App;

public partial class App : Application
{
    private static readonly HttpClient UpdateHttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _traySessionItem;
    private DispatcherTimer? _syncTimer;
    private MainViewModel? _syncViewModel;
    private WindowIcon? _icon;
    private bool _allowExit;
    private bool _exitConfirmationPending;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Nothing should ever reach the operator as a bare crash with no record on disk.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Record("Unhandled exception", args.ExceptionObject as Exception
                ?? new InvalidOperationException(args.ExceptionObject?.ToString() ?? "unknown"));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Record("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Stagecoach owns live helper processes, so the app never dies just because the last
            // window was hidden. Only an explicit Exit shuts it down.
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
            var updates = new GitHubReleaseUpdateService(
                UpdateHttpClient,
                Path.Combine(StagecoachPaths.RootDirectory, "updates"),
                CurrentProductVersion(),
                new WindowsUpdateInstallerLauncher());
            var settingsStore = new AppSettingsStore(Path.Combine(StagecoachPaths.RootDirectory, "settings.json"));
            var viewModel = new MainViewModel(
                store, identityService, discovery, credentials, connections,
                readiness, remediation, updates, settingsStore);

            _icon = StagecoachIcon.Create();
            var window = new MainWindow { DataContext = viewModel, Icon = _icon };
            desktop.MainWindow = window;
            ConfigureTray(desktop, window, viewModel);

            window.Opened += async (_, _) =>
            {
                // Ownership is settled before anything reads the database, because the passphrase
                // chosen here is what wraps its key. First run configures the owner; every later
                // start verifies it. Quitting either leaves the estate unread.
                if (!AppOwner.IsConfigured)
                {
                    var setup = new OwnerSetupWindow(cli) { Icon = _icon };
                    window.Hide();
                    setup.Show();
                    var entropy = await setup.Result;
                    if (entropy is null)
                    {
                        Exit(desktop);
                        return;
                    }

                    store.UseAdditionalEntropy(entropy);
                    store.RewrapKey(entropy);
                    window.Show();
                    window.Activate();
                }
                else
                {
                    var unlock = new UnlockWindow(cli) { Icon = _icon };
                    window.Hide();
                    unlock.Show();
                    var entropy = await unlock.Result;
                    if (entropy is null)
                    {
                        Exit(desktop);
                        return;
                    }

                    store.UseAdditionalEntropy(entropy);
                    window.Show();
                    window.Activate();
                }

                await viewModel.InitializeAsync();
                ApplyTheme(viewModel.SelectedTheme);
                ApplyAccent(viewModel.SelectedAccent);
                ConfigureBackgroundSync(viewModel);
                UpdateTrayStatus(viewModel);
                if (viewModel.StartMinimized) HideToTray(window);
            };

            // Windows Installer cannot replace a running executable, so an update closes the
            // application once elevation has been granted. This is the one shutdown that bypasses
            // the live-session guard, because the operator has already confirmed it.
            viewModel.ShutdownForUpdateRequested += () => Dispatcher.UIThread.Post(() => Exit(desktop));

            window.Closing += (_, args) =>
            {
                if (_allowExit) return;
                args.Cancel = true;
                var exitOnClose = viewModel.SelectedCloseBehavior == CloseBehavior.Exit;
                if (WindowLifecyclePolicy.ShouldExitOnClose(exitOnClose, viewModel.ActiveSessionCount))
                {
                    Exit(desktop);
                    return;
                }

                if (exitOnClose)
                    viewModel.StatusMessage =
                        $"Stagecoach stayed running — {viewModel.ActiveSessionCount} session(s) are still open.";
                HideToTray(window);
            };

            window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Window.WindowStateProperty &&
                    WindowLifecyclePolicy.ShouldHideOnMinimize(viewModel.MinimizeToNotificationArea, window.WindowState))
                    HideToTray(window);
            };

            viewModel.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(MainViewModel.SelectedTheme):
                        ApplyTheme(viewModel.SelectedTheme);
                        break;
                    case nameof(MainViewModel.SelectedAccent):
                        ApplyAccent(viewModel.SelectedAccent);
                        break;
                    case nameof(MainViewModel.BackgroundSyncEnabled):
                    case nameof(MainViewModel.BackgroundSyncMinutes):
                        ConfigureBackgroundSync(viewModel);
                        break;
                    case nameof(MainViewModel.StatusMessage):
                    case nameof(MainViewModel.IsBusy):
                    case nameof(MainViewModel.ActiveSessionCount):
                        UpdateTrayStatus(viewModel);
                        break;
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string CurrentProductVersion()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void ConfigureTray(IClassicDesktopStyleApplicationLifetime desktop, Window window, MainViewModel viewModel)
    {
        var show = new NativeMenuItem("Show Stagecoach");
        _traySessionItem = new NativeMenuItem("No sessions running") { IsEnabled = false };
        var sessions = new NativeMenuItem("Sessions");
        var sync = new NativeMenuItem("Sync now");
        var exit = new NativeMenuItem("Exit");
        var menu = new NativeMenu();
        menu.Add(show);
        menu.Add(_traySessionItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(sessions);
        menu.Add(sync);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Stagecoach — starting",
            Menu = menu,
            Icon = _icon,
            IsVisible = true,
        };
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
        sessions.Click += (_, _) =>
        {
            viewModel.SelectedTabIndex = 3;
            Show();
        };
        sync.Click += async (_, _) => await viewModel.SyncEstateAsync();
        exit.Click += (_, _) =>
        {
            // Exiting kills every helper process, so a live session earns one confirmation.
            if (WindowLifecyclePolicy.RequiresExitConfirmation(viewModel.ActiveSessionCount) && !_exitConfirmationPending)
            {
                _exitConfirmationPending = true;
                viewModel.SelectedTabIndex = 3;
                viewModel.StatusMessage =
                    $"{viewModel.ActiveSessionCount} session(s) are still running. Choose Exit again to close them.";
                Show();
                return;
            }

            Exit(desktop);
        };
    }

    private void UpdateTrayStatus(MainViewModel viewModel)
    {
        if (_trayIcon is null) return;
        _trayIcon.ToolTipText = WindowLifecyclePolicy.DescribeTrayStatus(
            viewModel.ActiveSessionCount, viewModel.IsBusy, viewModel.StatusMessage);
        if (_traySessionItem is not null)
            _traySessionItem.Header = viewModel.ActiveSessionCount switch
            {
                0 => "No sessions running",
                1 => "1 session running",
                var count => $"{count} sessions running",
            };
        if (viewModel.ActiveSessionCount == 0) _exitConfirmationPending = false;
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
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }

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
        AppAccent.Green => Color.Parse("#27715B"),
        AppAccent.Purple => Color.Parse("#7651A8"),
        _ => Color.Parse("#9A412B"),
    });
}
