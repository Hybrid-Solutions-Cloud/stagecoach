# stagecoach — Agent instructions

## What this repo is

Stagecoach: an installable native Windows one-click RDP/SSH launcher for Azure
VMs behind Bastion, Azure Arc/Azure Local machines, and direct-reachable Azure
VMs. Avalonia + .NET 10 desktop app modeled on Vault Prospector. Read
`REPO-INTENT.md` first, then the accepted plan at
`pmo/plans/stagecoach-design.md`.

---

## Start here — connect to the HCS Governance MCP

This repo is governed by the **HCS Governance MCP server**. It is the source of
truth for standards, hard rules, and orchestration guidance.

**At session start, call:**

```
bootstrap(repo="stagecoach", client="<your client: claude-code | codex | gemini | cursor | vscode>")
```

**Prefer a live MCP answer over anything written in this file** — this file is
the offline fallback.

---

## Offline fallback (when the MCP server is unreachable)

**Standards scope:** `hcs`

**Hard rules digest:**

- No secrets, tokens, passwords, subscription/tenant/client IDs, or connection strings in any committed file. All secrets live in `kv-hcs-vault-01` (or the relevant per-tenant vault), referenced by name only.
- All scripts: PowerShell 7+ — `#Requires -Version 7.0`, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'`. Never PS 5.1, never Bash.
- All documentation is Markdown only. Diagrams are draw.io only — commit the `.drawio` XML alongside any exported `.png`.
- Commit format: `type(scope): short description` — types `feat`, `fix`, `docs`, `chore`, `refactor`, `test` — with an `AB#<id>` work-item reference.
- Agents and automation run under the Claude Code subscription/harness only — never call a model API directly.

**Standards reference (public site — no auth required):**
<https://platform.hybridsolutions.cloud/standards/>

---

## Session protocol

1. **Read `.ai/state/` first** — `CURRENT_TASK.md`, then `HANDOFF.md`, then `OPEN_QUESTIONS.md`.
2. Then read `.ai/memory/` for durable context (`PROJECT_CONTEXT.md`, `DECISIONS.md`, `COMMANDS.md`, `GOTCHAS.md`).
3. Summarise your believed state back to the operator before making changes.
4. **Before ending the session, update `.ai/state/HANDOFF.md`** — what changed, files touched, commands run and results, branch, blockers, next steps.

---

## Repo-specific rules

- The accepted product is a native Windows application. The earlier
  PowerShell/Pode/single-file React design is superseded.
- Azure identity profiles use isolated Azure CLI configuration directories;
  never reuse or modify the operator's default Azure CLI profile.
- Target passwords belong only in Windows Credential Manager or an explicitly
  configured just-in-time provider; never SQLite, JSON, logs, arguments, or RDP files.
- Every helper/client process is owned by the session registry and cleaned up.
- Stagecoach is read-only against Azure (discovery reads + connection
  establishment). Any Azure write operation requires explicit operator
  confirmation and an explicit opt-in UI path.
- Never log, render, or persist a resolved credential; clipboard staging must
  auto-clear.

## Key facts

| Fact | Value |
|---|---|
| Org | Hybrid-Solutions-Cloud (GitHub) |
| Key Vault | kv-hcs-vault-01 |
| Work item format | `AB#<id>` in commits and PRs |
| Plan | `pmo/plans/stagecoach-design.md` |
