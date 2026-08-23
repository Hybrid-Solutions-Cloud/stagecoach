# About Stagecoach

**One login. Every VM. One click.**

Stagecoach is a local, clickable launcher for the connections operators make
all day long: RDP and SSH sessions into machines scattered across Azure
tenants. You sign in once with your Entra ID account, Stagecoach scans every
tenant that identity belongs to, and shows you one list of every VM you can
actually reach. Click a machine and the right kind of session opens — using
your current authentication, over whichever secure route exists.

## The problem it replaces

Connecting to Azure-attached machines means remembering three different Azure
CLI command families, each with its own flags and prerequisites:

| Target | Route | Command family |
|---|---|---|
| Azure VM behind Azure Bastion | Bastion native client | `az network bastion rdp` / `ssh` / `tunnel` |
| Azure Arc-enabled server or Azure Local VM | Arc SSH relay — no public IP, no inbound ports | `az ssh arc` (`--rdp` for Windows) |
| Azure VM with direct reachability | Public/private IP | `az ssh vm`, `mstsc` |

In practice that becomes a folder of per-target scripts and a lot of copying
resource IDs. Stagecoach discovers the estate, works out which routes each
machine supports, and runs the right command for you.

## How it works

- **Sign in once.** Stagecoach drives `az login` with your Entra ID and reuses
  that session for everything — discovery, connections, and credential
  lookups. It stores no credentials of its own.
- **Scan everything.** One Azure Resource Graph sweep per tenant finds every
  Azure VM and Arc machine your identity can see, then a capability decision
  tree works out how each one can be reached and why.
- **Click to connect.** Every connection launches PowerShell 7 running the
  Stagecoach module, which invokes the right `az` command. Long-running
  tunnels run as managed background processes — no leftover console windows,
  and parallel sessions just work.
- **Passwords handled properly.** When a local administrator password is
  needed, a resolver chain tries Entra Windows LAPS, then an Azure Key Vault
  secret, then falls back to a prompt — retrieved with your own RBAC, never
  persisted, cleared from the clipboard automatically.

## What it is not

Stagecoach is not a hosted service, not multi-user, and not a credential
store. It runs on your own workstation with your own tokens, and it is
read-only against Azure apart from establishing connections.

## Part of the Hybrid Solutions Cloud fleet

Stagecoach rides alongside the other HCS tools: [AzureScout](https://labs.hybridsolutions.cloud/azure-scout/)
finds and assesses the estate; Stagecoach takes you to a machine in it.

- Source: [github.com/Hybrid-Solutions-Cloud/stagecoach](https://github.com/Hybrid-Solutions-Cloud/stagecoach)
- License: MIT
- Status: in design and early build — see the [roadmap](./roadmap)
