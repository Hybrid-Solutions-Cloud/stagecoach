# Current Task: Stagecoach Desktop Command Center Complete (Vault Prospector Model)

- Repo: `Hybrid-Solutions-Cloud/stagecoach`
- Plan: `pmo/plans/stagecoach-implementation-plan.md` & `pmo/plans/stagecoach-design.md`
- ADO Project: `HCS - Stagecoach` (AB# tracking pending)

## Status: COMPLETE
- Architecture: Modeled after **Vault Prospector** as a persistent Desktop Command Center.
- Multi-Identity Hub: Dedicated **Identities & Sync** tab supporting multiple Entra accounts and selective tenant syncing.
- Local Persistent Metadata Cache: Stores discovered VMs in local cache (`~/.stagecoach/inventory.json` + `localStorage`) for instant sub-millisecond search upon launch.
- Features: Favorites pinning, Recent connection history, multi-column search/filters, Domain vs Workgroup auto-detection, and "Save to Key Vault" opt-in writeback.
- Backend: PowerShell 7 daemon handling `az` CLI, LAPS/Domain/Key Vault credential resolution, and native MSTSC subprocesses.
- Quality Gates:
  - Pester 6: 4/4 passing unit tests.
  - PSScriptAnalyzer: 0 errors, 0 warnings.
  - VitePress: Clean build (`npm run docs:build`).
