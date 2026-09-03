# Changelog

All notable changes to Stagecoach are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Machines is the landing screen.** The application opens directly on the machine list rather
  than a settings surface.
- **Tenant, subscription, source, OS, and state filters**, plus Favorites / Ready only / Pinned
  quick toggles and a Reset action.
- **Source column** distinguishing Azure, Arc, and Azure Local at a glance, alongside tenant and
  subscription columns.
- **Pinned local accounts.** Edit on any machine pins a stored local account so connecting never
  asks. Unpinned machines ask once and remember.
- **Local accounts** as a first-class section, replacing the mapping-rule builder. Username format
  alone determines the account type.
- **In-app updates** — check, SHA-256 verification against the authenticated GitHub digest, and
  Windows Installer launch, from Settings.
- **Session-aware lifecycle.** The tray shows the live session count, Exit asks for confirmation
  while sessions run, and closing the window never tears down live sessions.
- Actionable error banner with an accessible live region.
- Guide pages for [the interface](../guide/interface.md) and
  [updating Stagecoach](../guide/updates.md).

### Changed

- **Arc RDP now uses one local account for both the SSH relay and the Remote Desktop sign-in.**
  Previously the relay and desktop identities were modelled separately, which meant being prompted
  twice for an Arc machine. This supersedes the two-identity model in the accepted design.
- Window shell rebuilt on the Vault Prospector pattern: header band, active-account context strip,
  left product navigation, and a status bar.
- **Minimum window size reduced from 1080x680 to 320x300**, with compact density and flat, square,
  opaque surfaces, so the window fits a laptop and renders cleanly inside an RDP session.
- Machine list moved from a data grid to an aligned list, which removes horizontal scrolling and is
  cheaper to redraw remotely.
- Scope refresh and machine rescan are now two distinct actions instead of one ambiguous sync.
- Full design-token set and named style classes introduced in the application stylesheet.

### Removed

- The connection-identity mapping rule builder — scope kinds, match values, priority numbers, and
  the Arc relay checkbox. Pinning replaces all of it.
- Stale documentation navigation entries pointing at the superseded PowerShell design.

## [0.1.0]

### Added

- First native Windows implementation: Avalonia on .NET 10, WiX MSI and self-contained ZIP.
- Isolated Azure CLI profile per Entra account, with Web Account Manager and device-code sign-in.
- Azure Resource Graph discovery and correlation across Azure VMs, Arc machines, Azure Local,
  network interfaces, virtual networks, peerings, Bastion hosts, and extensions.
- Direct, Bastion, and Arc RDP and SSH orchestration with managed helper processes.
- SQLCipher-encrypted metadata with a DPAPI-protected key.
- Windows Credential Manager credential storage and temporary endpoint credential staging.
- Governed Arc OpenSSH remediation preview requiring explicit approval.
