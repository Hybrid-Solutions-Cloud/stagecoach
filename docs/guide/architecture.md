# Architecture

Stagecoach is a native Windows desktop application: Avalonia on .NET 10, packaged x64,
self-contained. There is no service, no web server, and no shared broker. All state belongs to the
signed-in Windows user.

## Projects

| Project | Responsibility |
|---|---|
| `Stagecoach.App` | Avalonia shell, machine list, tray and window lifecycle, themes |
| `Stagecoach.Core` | Domain models, application contracts, release update service |
| `Stagecoach.Infrastructure` | Azure CLI profiles, Resource Graph discovery, encrypted SQLite, Windows Credential Manager, process and session orchestration, update installer launcher |
| `Stagecoach.AskPass` | Narrow OpenSSH password bridge |
| `Stagecoach.Tests` | Domain, persistence, routing, lifecycle, and update-verification tests |
| `installer/` | WiX v5 MSI |
| `scripts/` | PowerShell 7 build, run, and packaging commands |

## Identity isolation

Each Entra account gets its own `AZURE_CONFIG_DIR` under `%LOCALAPPDATA%\Stagecoach`. Azure CLI
uses Web Account Manager for interactive sign-in and encrypts its own MSAL token cache inside that
directory. Stagecoach never reads token files, never imports your default Azure CLI context, and
never modifies `~/.azure`.

Isolation is also why one account's expired session cannot block discovery for another.

## Discovery

Azure Resource Graph reads, per included subscription:

- `Microsoft.Compute/virtualMachines`
- `Microsoft.HybridCompute/machines`
- `Microsoft.Network/bastionHosts`
- network interfaces, IP configurations, public IPs, virtual networks
- virtual network peerings in both directions
- VM and Arc extensions relevant to Entra sign-in and OpenSSH
- Hybrid Connectivity endpoints where visible

A machine seen by two accounts is one row with two access paths, not two rows.

## Storage

| Data | Store |
|---|---|
| Machines, access paths, scope, favourites, pins, settings | SQLCipher-encrypted SQLite under `%LOCALAPPDATA%\Stagecoach` |
| Database key | 256-bit key protected with Windows DPAPI at `CurrentUser` scope |
| Local account passwords | Windows Credential Manager |
| Azure tokens | Azure CLI's own encrypted cache, inside each isolated profile |

State is not portable to another Windows account, by design.

## Sessions

Helpers start with redirected output, hidden windows, and no secret-bearing command line. A session
registry tracks the process tree, endpoint, local port, route, start time, and health. Closing a
Remote Desktop or SSH client reaps its helper and returns its port. Exiting the application is
guarded while any session is live.

## Window shell

One window: header band, context strip showing the active account, an actionable error banner,
content, status bar. Navigation is a left product strip — Machines, Connect identities, Local
accounts, Sessions, Settings — with Machines as the landing screen.

The window shrinks to 320 × 300 and uses compact density with flat, square, opaque surfaces so it
fits a laptop and renders cleanly inside an RDP session.

## Security boundaries

- Discovery is read-only. The single Azure write — Arc OpenSSH deployment — requires an explicit
  preview and confirmation.
- No password reaches SQLite, JSON, logs, arguments, or `.rdp` files.
- Diagnostics carry stable local correlation IDs and redacted error categories, never credentials
  or tokens.
- A connection never silently falls back to a different Azure or local account.
- Updates only come from the signed release repository and are hash-verified twice before running.
