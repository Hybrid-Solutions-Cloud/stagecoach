<p align="center">
  <img src="docs/public/images/stagecoach-banner.svg" alt="Stagecoach — One login. Every VM. One click." width="640">
</p>

# Stagecoach

> One login. Every VM. One pick.

Sign in once with your Entra ID. Stagecoach discovers every machine your
identity can reach — Azure VMs behind Azure Bastion, Azure Arc-enabled
servers, and direct-reachable Azure VMs — and opens the right RDP or SSH
session with your current authentication. Previous logins are saved (target,
method, username — never passwords) so reconnecting is one keystroke.

## Quick start

```powershell
git clone https://github.com/Hybrid-Solutions-Cloud/stagecoach.git
cd stagecoach
Import-Module ./src/AzureStagecoach/AzureStagecoach.psd1
Start-Stagecoach
```

On Windows you can instead double-click **`Stagecoach.cmd`**, or run
`pwsh ./scripts/Install-Stagecoach.ps1` once to get a desktop shortcut.

`Start-Stagecoach` opens the **clickable web UI** in your browser
(single-file `stagecoach.html` with vendored React — no build step, served on
`127.0.0.1` only with a per-launch token):

1. It checks the Azure CLI and its `resource-graph`, `ssh`, and `bastion`
   extensions — one click installs anything missing.
2. **Sign in with Microsoft** runs `az login`; sign in once, ride everywhere.
3. Your **recent logins** sit at the top for one-click reconnects; below them
   the estate grid lists every machine with its route (Bastion, Arc relay, or
   direct) and the connect buttons that actually work for it.
4. Every connect click spawns `pwsh` running `Connect-StagecoachVM` — the
   browser never executes commands itself. Live tunnels and sessions show in
   the sessions panel with a Stop button.

## How each machine is reached

| Target | Route |
|---|---|
| Azure VM with a Bastion host in its VNet (or subscription) | `az network bastion rdp` / `ssh` / `tunnel` |
| Azure Arc-enabled server | `az ssh arc` (`--rdp` for RDP over the SSH relay) |
| Azure VM without Bastion | `mstsc /v:<ip>` (RDP) / `az ssh vm` (SSH) |

Auto method: Windows targets get RDP, everything else SSH — override with
`-Method Rdp|Ssh|Tunnel`. On a non-Windows client, RDP routes fall back to a
Bastion tunnel you can point any RDP client at.

## Cmdlets

| Cmdlet | What it does |
|---|---|
| `Start-Stagecoach` | The front door: local web UI (sign-in → scan → grid → connect drawer → sessions) |
| `Connect-StagecoachVM` | Connect to a machine by name, id, or piped target |
| `Get-StagecoachSession` / `Stop-StagecoachSession` | List / stop live tunnels and sessions |
| `Get-StagecoachInventory` | Discover machines, IPs, and Bastion mapping via Resource Graph (`-Cached` for offline) |
| `Get-StagecoachSavedConnection` / `Remove-StagecoachSavedConnection` | Manage saved logins (`~/.stagecoach/connections.json`) |
| `Connect-StagecoachAccount` | `az login` wrapper (tenant / device-code options) |
| `Test-StagecoachPrerequisite` | Check az CLI, sign-in, extensions; `-InstallMissing` to fix |
| `Enable-StagecoachArcSsh` | Create the Arc connectivity endpoint + SSH service config (confirms first) |
| `Install-StagecoachOpenSsh` | Install the WindowsOpenSSH extension on an Azure VM or Arc server that needs it (confirms first) |
| `Get-StagecoachCredential` | Optional resolver: Entra LAPS → Key Vault secret → nothing |

Stagecoach never persists credentials: saved logins hold usernames only, and
Azure writes (Arc SSH enablement, OpenSSH extension install) always prompt
for confirmation before touching anything.

## Prerequisites

- PowerShell 7.4+ (`pwsh`)
- Azure CLI (`az`) — Stagecoach installs the required CLI extensions itself
- Windows for RDP flows (mstsc); macOS/Linux clients get SSH and tunnels

Good to know:

- Bastion native-client connections need the **Standard SKU or higher** with
  native client support enabled; Developer/Basic SKUs cannot do CLI connections.
- Windows Arc servers need an SSH server — `Install-StagecoachOpenSsh` sets
  one up, and `Enable-StagecoachArcSsh` wires up the Azure side.
- Entra-ID SSH certificates work on Linux targets (AADSSHLoginForLinux);
  Windows targets use `-LocalUser`.

## Development

```powershell
pwsh ./scripts/Test.ps1   # PSScriptAnalyzer + Pester unit tests
```

The module lives in `src/AzureStagecoach` (Classes / Private / Public), tests
in `tests/Unit`. See [`REPO-INTENT.md`](REPO-INTENT.md) and the plan at
[`pmo/plans/stagecoach-design.md`](pmo/plans/stagecoach-design.md).

## License

MIT — see [LICENSE](LICENSE).
