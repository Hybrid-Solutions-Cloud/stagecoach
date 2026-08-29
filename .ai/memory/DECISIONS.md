# Decisions

- **Name `stagecoach` (no `azure-` prefix)** — operator choice, 2026-08-23; org context already scopes it.
- **Frontend stays one file** (`stagecoach.html`, vendored UMD React + htm) — no build pipeline without an explicit operator decision.
- **PowerShell fires every session** — UI clicks call the local API, which spawns `pwsh` running the connection cmdlet; the browser never executes commands.
- **Private repo at creation** — per HCS governance "private by default"; publish is a later deliberate decision.
- **Credential order** — LAPS (auto-rotated) beats a stale Key Vault copy when both exist; save-to-vault write-back is opt-in and off by default.
- **Web UI per the accepted plan; WPF stays retired (2026-08-29)** — the
  operator rejected the console-only front door: the product is the plan's
  local-first web app (`stagecoach.html`, vendored UMD React + htm, one file,
  no build step) on a 127.0.0.1-only backend with a per-launch token. The WPF
  desktop app and the old CORS-`*` listener remain removed. Deviation from the
  plan: the backend is stdlib HttpListener rather than Pode (PSGallery is
  unreachable from the build sandbox; the API surface is Pode-agnostic and can
  be swapped later). Every connect click spawns `pwsh` running
  `Connect-StagecoachVM`.
- **Saved logins, never saved secrets** — `~/.stagecoach/connections.json`
  stores target + method + username + usage stats only; passwords are never
  written. Azure state changes (Arc SSH enablement, OpenSSH extension install)
  are `ConfirmImpact = High` cmdlets that always prompt.
