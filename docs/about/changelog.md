# Changelog

All notable changes to Stagecoach are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.6.1]

### Removed

- **The passphrase is gone.** Stagecoach no longer asks for one, at setup or at startup. Vault
  Prospector never did, and matching it was the point: the local database is encrypted with a key
  protected by Windows for the owning account, and opening the application is gated by a presence
  check rather than a secret you have to invent and remember.
- The Settings passphrase lock, which had a defect that would have made the database key impossible
  to unwrap on the following launch.

### Changed

- **Unlock is a presence check.** A Windows owner is verified by Windows Hello, falling back to a
  Windows credential prompt — your own Windows account, checked by Windows and confirmed to be the
  account already signed in. Hello cannot prompt inside a remote session, so that fallback is what
  makes Stagecoach usable over RDP. An Entra owner signs in to the owning account.
- First-run setup asks only who owns the installation.
- Settings shows who owns the installation and how it is protected.

### Migration

An installation that already set a passphrase is asked for it once, so the database key can be
unwrapped and re-protected without it, and never again. **Start fresh** removes the local estate and
owner if the passphrase cannot be recalled; nothing in Azure is changed and machines are
rediscovered on the next scan.

## [0.6.0]

### Added

- **An owner account for the installation**, chosen at first run and separate from the Entra
  identities used to discover machines — a Windows account or an Entra account.
- **Entra sign-in detection.** Machines carrying `AADLoginForWindows`, `AADSSHLoginForLinux`, or the
  Arc equivalent show **Entra sign-in** rather than asking for a local account.

### Changed

- **Quick Connect rebuilt as a wizard that uses nothing the application has stored** — a throwaway
  sign-in, tenant, optional subscription, route, optional name, and a typed in-guest account that is
  removed immediately after launch.
- The machine list scrolls with the window instead of clipping to a fixed-height box.

## [0.5.0]

### Added

- **Activity page** — an audit log of when each scan ran, what it found, errors, and connection
  attempts. Never credentials or tokens.
- **Export and import settings** in Settings, to move an installation to another laptop. Never
  includes the database key, Credential Manager entries, or Azure token caches.
- **Quick Connect** for reaching a machine once without adding it to the estate.

## [0.4.1]

### Fixed

- **Azure Resource Graph discovery failed for every account, in every release.** Two independent
  defects: the KQL was passed as `--query`, a client-side filter, rather than `--graph-query`; and
  it was multi-line, which `az.cmd` truncates.
- Excluding a tenant now greys out its subscriptions and removes them from what discovery scans.

## [0.4.0]

### Added

- Rename a connected account; include all / exclude all for tenants and subscriptions.
- Updates can close a running Stagecoach rather than failing against it.

### Fixed

- Selecting a row in a list no longer blanks it.

## [0.3.0] – [0.3.4]

### Fixed

- **The Azure CLI was never launched at all.** The process was started as bare `az`, and
  `CreateProcess` does no `PATHEXT` resolution, so every Azure operation failed. (0.2.3)
- Adding an account failed every time after a successful sign-in — the profile move created its own
  destination first. (0.3.2)
- "Already configured" for an account that was never listed: it was saved, subscription enumeration
  then threw, and the duplicate check blocked every retry. (0.3.3)
- Buttons and navigation items disappeared under the pointer. Avalonia's Fluent theme resolves hover
  and pressed colours by resource key, not the control's background. (0.3.2, 0.3.4)
- Startup no longer blocks the interface on Azure CLI readiness checks. (0.3.1)
- Taskbar and installer icons. (0.2.2, 0.3.1)

### Added

- A real installer interface, first-run guidance, and support-bundle collection.

## [0.2.0]

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
