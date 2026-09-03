# Project context

Stagecoach is a native Windows Avalonia/.NET 10 desktop application modeled on Vault Prospector. It manages multiple isolated Azure CLI/Entra profiles, explicit tenant/subscription discovery scope, a merged Azure VM/Bastion/Arc/Azure Local estate, local accounts pinned per machine, and managed one-click RDP/SSH sessions.

The former PowerShell/Pode/single-file React design and incomplete WPF prototype are superseded. The accepted authority is `pmo/plans/stagecoach-design.md`.

## Published site

<https://labs.hybridsolutions.cloud/stagecoach/>

`labs.hybridsolutions.cloud` is a Cloudflare Worker (`hybrid-solutions-cloud-web`) that proxies the organisation's GitHub Pages origin and preserves the incoming path, so this repo stays a GitHub Pages project site with `base: '/stagecoach/'`. **Do not add a `CNAME` file** — that would make Pages serve on a custom domain and redirect the origin the Worker fetches. `hybrid-solutions-cloud.github.io/stagecoach/` is the origin, not the public URL.

Products are listed on the Labs landing page via `hybrid-solutions-cloud-web/app/ProjectCatalog.tsx`, using a relative `site:` path.

Binaries are published as GitHub releases on this repo and linked from `docs/download.md` through `releases/latest/download/`.
