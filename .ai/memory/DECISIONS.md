# Decisions

- **Name `stagecoach` (no `azure-` prefix)** — operator choice, 2026-08-23; org context already scopes it.
- **Frontend stays one file** (`stagecoach.html`, vendored UMD React + htm) — no build pipeline without an explicit operator decision.
- **PowerShell fires every session** — UI clicks call the local API, which spawns `pwsh` running the connection cmdlet; the browser never executes commands.
- **Private repo at creation** — per HCS governance "private by default"; publish is a later deliberate decision.
- **Credential order** — LAPS (auto-rotated) beats a stale Key Vault copy when both exist; save-to-vault write-back is opt-in and off by default.
