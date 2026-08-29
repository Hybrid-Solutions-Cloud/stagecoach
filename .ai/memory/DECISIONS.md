# Decisions

- **Name `stagecoach` (no `azure-` prefix)** — operator choice, 2026-08-23; org context already scopes it.
- **Frontend stays one file** (`stagecoach.html`, vendored UMD React + htm) — no build pipeline without an explicit operator decision.
- **PowerShell fires every session** — UI clicks call the local API, which spawns `pwsh` running the connection cmdlet; the browser never executes commands.
- **Private repo at creation** — per HCS governance "private by default"; publish is a later deliberate decision.
- **Credential order** — LAPS (auto-rotated) beats a stale Key Vault copy when both exist; save-to-vault write-back is opt-in and off by default.
- **Console-first, no web/desktop UI (v0.2.0 rebuild)** — operator declared the
  WPF desktop app + HttpListener web UI "doesn't work; redo it" (2026-08-29).
  The rebuilt product is the PowerShell module itself with an interactive
  `Start-Stagecoach` menu; the WPF solution and `stagecoach.html` were removed
  (the listener also exposed localhost APIs with CORS `*`). Reintroducing any
  GUI is a new, explicit operator decision.
- **Saved logins, never saved secrets** — `~/.stagecoach/connections.json`
  stores target + method + username + usage stats only; passwords are never
  written. Azure state changes (Arc SSH enablement, OpenSSH extension install)
  are `ConfirmImpact = High` cmdlets that always prompt.
