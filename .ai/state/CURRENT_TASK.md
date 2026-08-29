# Current Task: Console-first rebuild of Stagecoach (v0.2.0)

- Repo: `Hybrid-Solutions-Cloud/stagecoach`
- Branch: `claude/azure-vm-login-tool-m4lg45`
- Operator ask (2026-08-29): "This current solution doesn't work — redo it. Easy
  login, saved previous logins, easy access to Azure VMs behind Bastion and Arc
  VMs, install any OpenSSH extensions if needed."

## Status: rebuilt and validated (container-level)

Delivered in this session:

- Removed the non-working WPF .NET 9 desktop app, the HttpListener web UI
  (CORS `*` exposure), and ~465 committed `bin/`/`obj/` build artifacts.
- Rebuilt `src/AzureStagecoach` as a console-first PS7 module:
  - `Start-Stagecoach` interactive menu (prereqs → Entra sign-in → recents →
    picker → connect).
  - `Get-StagecoachInventory` now joins NIC IPs and maps Bastion hosts by VNet
    (subscription fallback) — the old version never populated these, so the
    Bastion route could never fire.
  - `Connect-StagecoachVM` routes: Bastion rdp/ssh/tunnel, `az ssh arc [--rdp]`,
    `az ssh vm`, direct mstsc; SSH runs inline, RDP/tunnel detached.
  - Saved logins in `~/.stagecoach/connections.json` (no passwords).
  - `Test-StagecoachPrerequisite -InstallMissing` (resource-graph/ssh/bastion),
    `Enable-StagecoachArcSsh`, `Install-StagecoachOpenSsh` (both ConfirmImpact
    High — Azure writes prompt).
- Tests: Pester suite rewritten (`tests/Unit/*`); `scripts/Test.ps1` gate.

## Next steps

1. Run `Start-Stagecoach` on the real Windows workstation against live tenants
   (container had no az CLI / PSGallery — validated via parse + module-scope
   smoke harness, 28/28 assertions).
2. Run `pwsh ./scripts/Test.ps1` where PSGallery is reachable (Pester 5).
3. Decide whether a web/desktop UI ever comes back (see DECISIONS.md).
