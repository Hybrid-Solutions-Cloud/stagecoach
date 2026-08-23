# Stagecoach

> One login. Every VM. One click.

Sign in once with your Entra ID. Stagecoach scans every tenant that identity
belongs to, lists every VM you can actually reach — Azure VMs behind Bastion,
Azure Arc-enabled servers (including Azure Local), and direct-reachable Azure
VMs — and one click opens the right RDP or SSH session with your current
authentication.

Under the hood every click launches PowerShell 7 running the Stagecoach module,
which invokes the right Azure CLI command (`az network bastion rdp/ssh/tunnel`,
`az ssh arc [--rdp]`, `az ssh vm [--rdp]`) and manages the long-running tunnel
helpers as hidden background processes — no leftover console windows.

## Status

🚧 **Bootstrap.** The accepted research/architecture/delivery plan lives at
[`pmo/plans/stagecoach-design.md`](pmo/plans/stagecoach-design.md). See
[`REPO-INTENT.md`](REPO-INTENT.md) for what this repo is and is not.

## Planned shape

| Piece | Choice |
|---|---|
| Backend | PowerShell 7 + Pode, localhost-only; also a plain PS module (`Connect-StagecoachVM`, `Get-StagecoachInventory`) |
| Frontend | Single-file React page (`stagecoach.html`, vendored UMD + htm — no build step) |
| Discovery | Azure Resource Graph per tenant, capability decision tree per machine |
| Credentials | Entra Windows LAPS → Key Vault secret → manual prompt; never persisted |
| Docs | VitePress site under `docs/` (coming soon) |

## Prerequisites (planned v1)

- Windows workstation (RDP flows use mstsc; macOS/Linux fall back to tunnels)
- PowerShell 7.4+
- Azure CLI with the `ssh` (≥ 2.0.4) and `bastion` extensions

## License

MIT — see [LICENSE](LICENSE).
