<p align="center">
  <img src="docs/public/images/stagecoach-banner.svg" alt="Stagecoach" width="640">
</p>

# Stagecoach

Stagecoach is a native Windows connection hub for administrators who operate across multiple Microsoft Entra tenants. Add one or more Entra identities, explicitly select the tenants and subscriptions each identity may scan, and use one clean estate view to connect to Azure VMs, Azure Arc-enabled servers, and Azure Local VMs.

It follows the Vault Prospector model: local-first, profile-aware, encrypted at rest, taskbar-notification-area capable, and organized around the operator rather than a browser or local web server.

## What works

- Separate, isolated Azure CLI authentication profile for each Entra identity
- Explicit tenant and subscription inclusion; newly discovered scope is disabled by default
- Azure Resource Graph discovery and de-duplication across identities
- Azure VM correlation with NICs, IP addresses, VNets, peering, and Azure Bastion
- Azure Arc and Azure Local discovery with WindowsOpenSSH readiness
- Direct RDP/SSH, Bastion tunnel/native client, and Arc SSH/RDP routing
- Domain, machine, tag, resource-group, subscription, and tenant credential mappings
- Separate target-login and Arc-relay identities
- Password storage in Windows Credential Manager and encrypted SQLCipher metadata
- Favorites, search, route override, managed sessions, background refresh, themes, and notification-area behavior
- Explicit preview and approval before Stagecoach deploys the WindowsOpenSSH Arc extension

## Quick start

Requirements: Windows 10 1809 or newer, Azure CLI, `mstsc.exe`, and the Windows OpenSSH client. The app can install or update its required Azure CLI extensions from **Settings**.

```powershell
pwsh ./scripts/Build.ps1
pwsh ./scripts/Run.ps1 -Configuration Release
```

For a self-contained ZIP and MSI:

```powershell
pwsh ./scripts/Package.ps1 -Version 0.1.0 -Installer
```

Then follow [the quickstart](docs/guide/quickstart.md).

## Authentication reality

Stagecoach removes repeated prompts where Windows and the connection protocol safely permit it. It does not bypass Conditional Access, MFA, PIM, expired Azure sessions, Remote Desktop policy, SSH host-key verification, or a server that rejects the mapped account. Entra sign-in and target-machine credentials are intentionally separate.

## Security boundary

Stagecoach runs as the signed-in Windows user. Azure token state lives in a separate Windows-encrypted Azure CLI profile per identity. Metadata is encrypted with SQLCipher using a DPAPI-protected key. Target passwords are stored only in Windows Credential Manager; temporary `TERMSRV` entries are removed when the managed session ends. No credential value is logged or written to the metadata database.

Azure discovery is read-only. The only Azure mutation in v1 is the optional WindowsOpenSSH Arc extension deployment, which requires a visible two-step confirmation.

See [required Azure access](docs/guide/quickstart.md#required-access), [architecture](docs/guide/architecture.md), and [the accepted design](pmo/plans/stagecoach-design.md).

## License

MIT — see [LICENSE](LICENSE).
