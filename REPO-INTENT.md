# Repo intent — stagecoach

**One login. Every VM. One click.**

## What this repo is

Stagecoach is a simple, clickable local tool for operators who RDP/SSH into many
kinds of Azure-connected machines every day. You sign in once with an Entra ID
account; Stagecoach scans every tenant that identity belongs to and lists every
VM you can actually reach; clicking one opens the right kind of session with
your current authentication:

- **Azure VMs behind Azure Bastion** — `az network bastion rdp / ssh / tunnel`
- **Azure Arc-enabled servers** (including Azure Local VMs) — `az ssh arc [--rdp]`
- **Plain Azure VMs** with direct reachability — `az ssh vm [--rdp]` / mstsc

It replaces the pile of per-target "parachute" scripts with one launcher that
still fires PowerShell underneath: every connect click spawns `pwsh` running
`Connect-StagecoachVM`, which invokes the right `az` command.

## Shape

- **Backend:** PowerShell 7 + Pode, bound to `127.0.0.1` only. Doubles as a
  plain PowerShell module usable without the UI.
- **Frontend:** one static `stagecoach.html` — single-file React (vendored UMD
  + htm), no build pipeline, no Node dependency.
- **Credentials:** resolver chain Entra Windows LAPS → Key Vault secret →
  manual prompt. Nothing is ever persisted by Stagecoach; reads use the
  operator's own RBAC and land in the vault audit log.
- **Sessions:** long-running tunnel/relay helpers run hidden or minimized as
  managed background processes — parallel sessions on a port pool, watchdog,
  orphan reaping, one-click reconnect. No leftover console windows.

## What this repo is not

- Not a hosted service and not multi-user — it runs on the operator's own
  workstation with the operator's own tokens.
- Not a credential store — no secrets, passwords, or IDs are ever committed or
  persisted (HCS hard rule).
- Not a replacement for azure-scout — scout finds and assesses the estate;
  stagecoach takes you to a machine in it.

## Where things are

- The full research, architecture, and delivery plan: `pmo/plans/stagecoach-design.md`
- Docs site (VitePress, planned): `docs/`
- Governance: HCS standards via the HCS Governance MCP (`bootstrap(repo="stagecoach", ...)`);
  offline fallback digest in `AGENTS.md`

## Status

Bootstrap. Plan accepted; implementation phases 0–4 are defined in the plan and
tracked via ADO work items (`AB#<id>`) once the Epic/Feature is created.
