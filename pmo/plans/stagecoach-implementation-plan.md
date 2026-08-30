# Stagecoach native Windows implementation plan

## Azure DevOps tracking

- Epic: AB#7937 — Stagecoach native Windows hybrid connection hub
- Feature: AB#7938 — Multi-Entra identity and explicit Azure scope management
- Feature: AB#7939 — Unified Azure VM, Bastion, Arc, and Azure Local discovery
- Feature: AB#7940 — Secure one-click connection and Arc remediation orchestration
- Feature: AB#7941 — Native Windows UX, packaging, testing, and administrator documentation

This plan implements the accepted design in `stagecoach-design.md`. A passing build is necessary
but never substitutes for live connection evidence.

## Phase 0 — Reconcile and establish the desktop foundation

- Remove the superseded PowerShell/Pode/browser product path and incomplete WPF scaffold.
- Create the .NET 10 Avalonia solution and WiX packaging skeleton.
- Add deterministic PowerShell 7 build/run/package scripts.
- Establish domain contracts, local paths, redacted diagnostics, and test projects.

**Exit:** clean restore/build/test; app opens; MSI/ZIP can be produced reproducibly.

## Phase 1 — Identity and scope hub

- Isolated `AZURE_CONFIG_DIR` profile per identity.
- WAM-first `az login`, device-code fallback, reauthenticate, and remove.
- Tenant/subscription enumeration and explicit selection.
- SQLite persistence and identity-isolated failures.
- First-run operator flow.

**Exit:** two real Entra accounts can coexist, retain independent scope, and refresh silently.

## Phase 2 — Estate discovery and correlation

- ARG queries for Azure VMs, Arc machines, NIC/IP/VNet/peering/Bastion/extensions.
- Parse and correlate access paths without leaking identifiers to logs.
- Combined cached estate with freshness and per-source errors.
- Search, filters, grouping, favorites, recents, and details drawer.

**Exit:** representative multi-tenant inventory is correct and identifies Bastion and Arc routes.

## Phase 3 — Connection identities and one-click launch

- Windows Credential Manager-backed connection profiles and mapping rules.
- Direct RDP with temporary endpoint credential staging.
- Bastion tunnel/native-client orchestration using the selected Azure identity profile.
- Arc/Azure Local SSH and RDP-over-SSH orchestration, including AskPass fallback.
- Session registry, port pool, watchdog, stop, reconnect, and cleanup.

**Exit:** supported direct, Bastion, and Arc Windows routes connect without redundant prompts when
the provider permits it; secrets never appear in arguments, files, database, or logs.

## Phase 4 — Readiness and governed remediation

- Workstation prerequisite diagnosis.
- Arc/OpenSSH/Hybrid Connectivity readiness diagnosis.
- Exact remediation preview, confirmation, execution, and re-scan.
- Permission and limitation documentation.

**Exit:** missing prerequisites are actionable; no Azure write occurs without explicit consent.

## Phase 5 — Windows product polish and distribution

- Notification-area lifecycle and background sync.
- System/light/dark themes, accessible accent palettes, density and RDP display settings.
- Installer lifecycle, upgrade, uninstall, shortcut/icon, and state-retention tests.
- Support bundle, privacy/redaction validation, and clean-machine walkthrough.

**Exit:** installable product meets the requirement-by-requirement release matrix in the design.

## Verification matrix

| Requirement | Automated evidence | Live/installed evidence |
|---|---|---|
| Multiple identities | isolated profile/store tests | two real Entra accounts |
| Tenant/subscription scope | parser/selection tests | real multi-tenant account |
| Combined estate | repository/query fixtures | representative subscriptions |
| Bastion correlation | topology fixtures | same-VNet and peered-VNet targets |
| Arc/Azure Local | ARG/readiness fixtures | connected Windows and Linux targets |
| Target credentials | Credential Manager/security tests | domain and local RDP accounts |
| One-click launch | process/argument/redaction tests | direct, Bastion, Arc sessions |
| Remediation safety | preview/confirmation tests | approved non-production target |
| RDP-session support | session-detection tests | installed app inside RDP/AVD |
| Tray/themes | view-model/UI tests | installed Windows walkthrough |
| Packaging | MSI/ZIP validators | clean install/upgrade/uninstall |
