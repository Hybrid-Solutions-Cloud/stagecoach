# Handoff

## Session 2026-08-29 (part 2) — clickable web UI per the accepted plan

Operator feedback mid-session: the console menu was wrong — the plan
(`pmo/plans/stagecoach-design.md`) specifies a clickable local web app. Part 2
delivered it on top of the part-1 module engine.

### What changed

1. **`Start-Stagecoach` is now the local web host** (Public/Start-Stagecoach.ps1):
   binds 127.0.0.1 on a random high port, per-launch bearer token passed in the
   URL fragment, no CORS headers; serves `stagecoach.html` + `/vendor/*`;
   JSON API: state, login, extensions/install, inventory, connections,
   connect, sessions, sessions/stop, arc/enable-ssh, openssh/install, shutdown.
   Every connect spawns `pwsh -Command Connect-StagecoachVM -Id ...` (SSH gets
   a real terminal window via -NoExit; RDP/tunnel helpers run minimized).
2. **`src/AzureStagecoach/Web/stagecoach.html`** — single-file React UI
   (vendored `Web/vendor/`: React 18.3.1 UMD, ReactDOM, htm 3.1.1 — from npm,
   committed, no build step): sign-in card (tenant + device-code options),
   prereq banner with one-click extension install, recent-logins row, estate
   grid (search, kind filter, connectable-only, route + why, per-capability
   buttons, dimmed rows with reasons), connect drawer (method radios, username
   with VM-admin hint, Arc SSH setup buttons with confirm), sessions panel.
3. **Session registry** — Private/StagecoachSessionStore.ps1 + public
   `Get-StagecoachSession` / `Stop-StagecoachSession`; Connect-StagecoachVM
   records detached helpers; the server records interactive SSH wrappers.
4. **Two real bugs found by testing**:
   - `$x = if (...) { @() }` collapses to `$null` under StrictMode → first-run
     inventory 500. Fixed by wrapping the whole conditional in `@()`.
   - Class-typed attributes on exported functions (`[OutputType([StagecoachSession])]`,
     `[StagecoachTarget]$Target`) resolve lazily from the CALLER's scope on
     first invocation → "Unable to find type" for any external caller,
     including the spawned pwsh children. Fixed: string-form OutputType and
     untyped public `$Target` params. **GOTCHA for future public cmdlets.**

### Validation (sandbox)

- Playwright + bundled Chromium drove the real UI against a fake `az` CLI
  (canned ARG/account/extension JSON): 7 machines, drawer connect + grid
  connect each spawned a pwsh child running the cmdlet, 2 live sessions
  tracked (registry pruning works), 2 saved logins created, 0 JS errors.
  Screenshots delivered to the operator.
- Module smoke harness: 28/28. All PowerShell files parse; import clean.
- Pester/PSSA still not runnable in sandbox (PSGallery egress-blocked) — run
  `pwsh ./scripts/Test.ps1` on the workstation.

### Branch

`claude/azure-vm-login-tool-m4lg45` — committed and pushed (parts 1 + 2).

### Next steps

1. Real-estate run on the Windows workstation (`Stagecoach.cmd`).
2. Phase 3 credential polish: LAPS/KV indicator in the drawer, clipboard
   staging with auto-clear (module resolver already exists).
3. Consider swapping HttpListener → Pode when installing deps is possible;
   SSE scan progress.
