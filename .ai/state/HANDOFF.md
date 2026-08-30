# Handoff

## Session 2026-08-30 — native Windows redesign delivered

### Branch

`feature/native-windows-redesign`

Pull request: <https://github.com/Hybrid-Solutions-Cloud/stagecoach/pull/1>

### Azure DevOps

- Closed Epic AB#7937
- Closed Features AB#7938, AB#7939, AB#7940, and AB#7941

### Outcome

Replaced the abandoned PowerShell/Pode/browser path and incomplete WPF/.NET 9 scaffold with the accepted Vault Prospector-style native Windows product. The app is Avalonia on .NET 10 and has a single clean estate view plus dedicated identity, connection-identity, session, and settings surfaces.

### Implemented

- Per-Entra-account isolated Azure CLI profiles with WAM or device-code login
- Deterministic duplicate prevention, reauthentication, removal, tenant/subscription inventory, and opt-in scope
- Azure Resource Graph discovery/correlation for Azure VM, NIC/IP/VNet/peering/Bastion, Arc, and Azure Local
- Multi-identity machine de-duplication with stale-path pruning
- Direct RDP/SSH, Bastion tunnel/native RDP/SSH, and Arc RDP/SSH orchestration
- SQLCipher metadata with DPAPI-protected key
- Windows Credential Manager connection profiles; temporary session-persistent RDP credentials; SSH AskPass helper
- Mapping by tenant, subscription, resource group, domain, tag, or machine, with separate Arc relay mappings
- Per-machine route override, search, favorites, recents/sessions, background sync, notification-area lifecycle
- System/light/dark themes plus Rust/Blue/Green/Purple accent schemes
- Two-step WindowsOpenSSH Arc remediation preview/approval; no silent Azure writes
- Workstation readiness and explicit Azure CLI extension preparation
- Self-contained ZIP/checksum and WiX MSI packaging
- Rewritten README, quickstart, architecture, connection, credential, release, roadmap, ADR, and plan documentation
- Removed 465 previously committed build artifacts and all superseded product code

### Verification

- `pwsh ./scripts/Build.ps1 -Configuration Release`: success; zero warnings/errors
- `dotnet test Stagecoach.sln -c Release`: 12 passed, 0 failed
- `dotnet format Stagecoach.sln --verify-no-changes --no-restore`: success
- `dotnet list Stagecoach.sln package --vulnerable --include-transitive`: no known vulnerable packages
- `git diff --check`: success
- `npm ci` and `npm run docs:build`: success after correcting the accepted-design link for VitePress
- Self-contained published executable: launched with a responsive `Stagecoach` main window
- MSI: silent install, installed-app launch/readiness check, and silent uninstall all passed
- ZIP SHA-256 sidecar matches the generated archive
- Secret-pattern scan returned no committed credential material

### Artifacts

- `artifacts/Stagecoach-0.1.0-win-x64.zip`
- `artifacts/Stagecoach-0.1.0-win-x64.zip.sha256`
- `installer/bin/Release/Stagecoach-0.1.0-win-x64.msi`

### Remaining external validation

Live Entra sign-in, subscription discovery, Bastion, Arc, Azure Local, target credential, Conditional Access, and WindowsOpenSSH deployment paths require representative authorized Azure resources and were intentionally not simulated or mutated during local release validation. The app and docs state the required access and interaction boundaries.

### Repository note

The native redesign is committed with the AB#7937 reference and pushed to the feature branch. PR #1's initial documentation check exposed a VitePress dead link; the link was corrected and the complete Release build, tests, ZIP, checksum, and MSI were regenerated successfully before merge.
