using System.Windows;
using Stagecoach.App.ViewModels;
using Stagecoach.App.Views;
using Stagecoach.Infrastructure.Azure;
using Stagecoach.Infrastructure.Orchestration;
using Stagecoach.Infrastructure.Storage;

namespace Stagecoach.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new SqliteMetadataStore();
        var discovery = new AzureCliDiscoveryService();
        var resolver = new KeyVaultCredentialResolver();
        var orchestrator = new ProcessOrchestrator();

        var viewModel = new MainViewModel(store, discovery, resolver, orchestrator);
        var mainWindow = new MainWindow(viewModel);

        mainWindow.Show();
    }
}
