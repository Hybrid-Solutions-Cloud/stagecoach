# stagecoach — Claude Code

@AGENTS.md

## Claude Code notes

- Follow the `.ai/` session protocol: read `.ai/state/*` at session start, and
  update `.ai/state/HANDOFF.md` before ending a session.
- Use **plan mode** before broad, repo-wide changes.
- See the [agents standard](https://platform.hybridsolutions.cloud/standards/agents/)
  for the full multi-model model.

## Claude Code actions in this repo

**Run autonomously:**
- Read, search, and grep any file in this repo
- Write and edit files in this repo
- `git add`, `git commit`, `git push`
- `az` CLI read operations: `az ... show`, `az ... list`

**Always confirm before:**
- Creating or deleting Azure resources
- Any `az` CLI write operation that modifies Azure state
- Running destructive operations
- Making API calls to external services
- Installing software
