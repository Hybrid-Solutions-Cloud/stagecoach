# Current Task: Stagecoach web UI delivered per the accepted plan (v0.2.0)

- Repo: `Hybrid-Solutions-Cloud/stagecoach`
- Branch: `claude/azure-vm-login-tool-m4lg45`
- Plan: `pmo/plans/stagecoach-design.md` — Phase 1 (core module) + Phase 2 (local web UI)

## Status: built and validated end-to-end in the sandbox

- **Phase 1 — module engine**: discovery with NIC/IP + Bastion-by-VNet mapping
  (subscription fallback), full connect routing (Bastion rdp/ssh/tunnel,
  `az ssh arc [--rdp]`, `az ssh vm`, direct mstsc), saved logins, az extension
  bootstrap, `Enable-StagecoachArcSsh` / `Install-StagecoachOpenSsh` (confirmed
  Azure writes), session registry (`~/.stagecoach/sessions.json`).
- **Phase 2 — clickable UI**: `Start-Stagecoach` hosts `stagecoach.html`
  (single file, vendored UMD React 18 + htm, no build step) on 127.0.0.1 with a
  per-launch token; sign-in card → estate grid (kind/OS badges, route + why,
  capability buttons, greyed rows with reasons) → connect drawer (method,
  username, Arc SSH setup buttons) → sessions panel with Stop. Every connect
  spawns `pwsh -Command Connect-StagecoachVM` (browser never runs commands).
- **Validated**: Playwright drove the real UI in Chromium against a fake `az` —
  7 machines rendered, drawer + grid connects spawned real pwsh children, live
  sessions tracked and stoppable, saved logins created; zero JS errors.
  Module smoke harness 28/28. All files parse; module imports clean.

## Known deviations from the plan (documented in DECISIONS.md)

- Backend is stdlib HttpListener, not Pode (PSGallery blocked in the sandbox);
  API is framework-agnostic, swap is possible later.
- Scan progress uses polling, not SSE, in v1.

## Next steps

1. Operator run on the Windows workstation: `Stagecoach.cmd` → sign in → connect
   one of each kind (Bastion RDP, Bastion SSH/tunnel, Arc SSH, Arc RDP).
2. `pwsh ./scripts/Test.ps1` where PSGallery is reachable (Pester 5 + PSSA).
3. LAPS/Key Vault drawer integration (plan Phase 3) still pending.
