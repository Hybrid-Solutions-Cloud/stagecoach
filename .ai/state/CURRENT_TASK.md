# Current Task: Stagecoach Native .NET 9 Desktop Application Complete

- Repo: `Hybrid-Solutions-Cloud/stagecoach`
- Plan: `pmo/plans/stagecoach-implementation-plan.md` & `pmo/plans/stagecoach-design.md`
- Solution: `Stagecoach.sln` (.NET 9 C# Desktop Application modeled after Vault Prospector)

## Status: COMPLETE
- Standalone Native Windows App: `Stagecoach.App.exe` built in Release mode.
- Architecture:
  - `Stagecoach.Core`: Domain models (`StagecoachMachine`, `StagecoachIdentity`, `StagecoachSession`) & Interfaces (`IDiscoveryService`, `ICredentialResolver`, `IMetadataStore`, `IProcessOrchestrator`).
  - `Stagecoach.Infrastructure`: SQLite metadata persistence (`SqliteMetadataStore`), Azure CLI discovery engine (`AzureCliDiscoveryService`), Key Vault / LAPS resolver (`KeyVaultCredentialResolver`), and Native process orchestrator (`ProcessOrchestrator`).
  - `Stagecoach.App`: WPF/XAML Desktop UI with MVVM CommunityToolkit, dark theme matching Vault Prospector, instant search, domain badges, favorites, recents, and slide-over Connect drawer.
  - `Stagecoach.Tests`: xUnit automated test suites (3/3 passed).
- Desktop Shortcut: `C:\Users\KristopherTurner\Desktop\Stagecoach.lnk` points directly to `Stagecoach.App.exe`.
