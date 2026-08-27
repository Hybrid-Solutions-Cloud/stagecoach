# Handoff

## Session 2026-08-27 — Stagecoach Native .NET 9 Desktop Application (Vault Prospector Model)

### What Changed
1. **Full .NET 9 Solution (`Stagecoach.sln`):**
   - Scaffolding matching `Vault Prospector` architecture.
   - `src/Stagecoach.Core`: Models (`StagecoachMachine`, `StagecoachIdentity`, `StagecoachSession`, `TargetKind`, `DomainType`, `CredentialResolution`) and Contracts (`IDiscoveryService`, `ICredentialResolver`, `IMetadataStore`, `IProcessOrchestrator`).
   - `src/Stagecoach.Infrastructure`:
     - `SqliteMetadataStore`: Embedded SQLite database (`~/.stagecoach/stagecoach.db`) with instant offline indexing, favorites, and recents.
     - `AzureCliDiscoveryService`: Multi-tenant ARG sweep driver.
     - `KeyVaultCredentialResolver`: Graph API LAPS + Key Vault secret resolver.
     - `ProcessOrchestrator`: Process manager for `mstsc.exe` and `az ssh arc`.
   - `src/Stagecoach.App`: High-performance WPF Desktop UI with dark theme, sidebar tabs (Estate, Identities & Sync, Sessions & Recents), CommunityToolkit MVVM, and slide-over Connect drawer.
2. **Build and Run Automation:**
   - `scripts/Build.ps1`: Builds solution and executes xUnit tests.
   - `scripts/Run.ps1`: 1-command launch of the desktop application.
   - `scripts/Install-StagecoachShortcut.ps1`: Configures `Stagecoach.lnk` on user's Desktop pointing directly to `Stagecoach.App.exe`.
3. **Automated Verification:**
   - `dotnet test Stagecoach.sln -c Release`: **3/3 passed (100%)**.
   - `dotnet build Stagecoach.sln -c Release`: **0 warnings, 0 errors**.
