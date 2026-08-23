# Roadmap

Stagecoach is being delivered in five phases. Dates firm up as each phase
completes; this page tracks the plan of record.

## Phase 0 — Spike *(next up)*

Prove every connection path end to end with hand-run PowerShell 7 scripts
against real infrastructure: Bastion RDP, Bastion tunnel, Arc RDP-over-SSH,
`az ssh vm`, and the multi-tenant Resource Graph sweep. Every quirk found
gets recorded before any product code is written.

## Phase 1 — Core module

The `AzureStagecoach` PowerShell module: sign-in and tenant handling,
estate inventory with the capability decision tree, session launching, and
the credential resolver. Fully usable from a terminal — the UI is optional
by design.

## Phase 2 — Local web UI

The single-file React page served by the local Pode backend: sign-in, tenant
picker, estate grid with per-machine connect buttons, the connect drawer, and
the live sessions panel. Background session management means no leftover
console windows and parallel sessions on a managed port pool.

## Phase 3 — Credential polish

Entra Windows LAPS retrieval, the Key Vault secret convention with per-VM
tag overrides, opt-in save-to-vault, clipboard auto-clear, favorites, and
per-machine local-user memory.

## Phase 4 — Distribution

PowerShell Gallery publication, a one-command `Start-Stagecoach` experience,
a winget manifest, and the full documentation site here.

## Done so far

- ✅ Research and accepted architecture plan
- ✅ Repository, brand, and this documentation site
- ✅ Azure DevOps project for work tracking
