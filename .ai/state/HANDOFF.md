# Handoff

## Session 2026-08-23 — repo bootstrap

Repo created in Hybrid-Solutions-Cloud and scaffolded per the HCS governance
standard: README, REPO-INTENT, AGENTS/CLAUDE context files, MIT LICENSE,
.gitignore, docs/index.md placeholder (VitePress site planned), the .ai/
workspace, and the accepted design plan at `pmo/plans/stagecoach-design.md`
(canonical copy; originally drafted in azure-scout branch
`claude/azure-vm-rdp-tool-akli2k`).

Open items: create the ADO Epic/Feature (AB#), set branch protection on main,
register the repo in the HCS platform registry, decide Windows-only v1 vs
macOS tunnel fallback, and confirm the Key Vault secret convention.

## Session 2026-08-23 (later) — VitePress coming-soon site

Added the VitePress site: `docs/.vitepress/config.ts` (base `/stagecoach/`),
home-layout `docs/index.md` (hero + coming soon), `package.json`
(`"type": "module"`, vitepress ^1.5.0) with lockfile, and
`.github/workflows/documentation.yml` mirroring azure-scout's pipeline
(HCS self-hosted runners, peaceiris gh-pages deploy). Local build verified.
One-time settings still needed: Pages source → `gh-pages` branch after the
first deploy run, and note Pages requires a paid plan while the repo is private.

## Session 2026-08-23 (later) — site published with About section

Pages is enabled with source "GitHub Actions"; the workflow was converted to
upload-pages-artifact + deploy-pages (peaceiris/gh-pages flow removed) and
runs on ubuntu-latest while the HCS runner fleet is offline. Deploys are
green; the site serves at labs.hybridsolutions.cloud/stagecoach/. The
landing page dropped its under-construction note, and the top nav gained an
About dropdown (About / Roadmap / Changelog / Release notes) with a matching
sidebar under docs/about/. Two stale runs queued on the offline self-hosted
fleet were cancelled. Still open: registry insert into master-registry.db,
ADO Epic/Feature (AB#), research spikes + ADRs, revert runners to the HCS
fleet when it returns.
