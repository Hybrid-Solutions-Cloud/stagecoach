<p align="center">
  <img src="docs/public/images/stagecoach-banner.svg" alt="Stagecoach" width="640">
</p>

# Stagecoach

**One identity hub. Every reachable machine. One click.**

**Documentation and downloads: [labs.hybridsolutions.cloud/stagecoach](https://labs.hybridsolutions.cloud/stagecoach/)**

Stagecoach is an installable native Windows application for administrators who work across
multiple Microsoft Entra tenants. Connect one or more Entra accounts, choose the tenants and
subscriptions each may scan, and get a single filterable list of every Azure VM, Azure Arc-enabled
server, and Azure Local machine you can reach. Pick one, and it connects.

It follows the Vault Prospector model: local-first, profile-aware, encrypted at rest, notification-area
capable, and built around the operator rather than a browser or a local web server.

## How it works

1. **Connect identities** — sign in with your Windows account, or add another Entra account. Each
   gets its own isolated, Windows-encrypted Azure CLI session.
2. **Include scope** — pick the tenants and subscriptions to scan. New scope is never added
   silently.
3. **Local accounts** — add the account you use inside the machines, as `DOMAIN\username` or plain
   `username`. The password goes to Windows Credential Manager.
4. **Pin and connect** — `Edit` a machine to pin a local account, then click **Connect**. The
   Azure CLI helper runs hidden and Remote Desktop opens. No console window, no credential prompt.

Arc machines behave identically: the same single local account covers both the SSH relay and the
Remote Desktop sign-in, so **you are never asked to enter a local administrator account for Arc**.

## What works

- Machine list as the landing screen, filtered by tenant, subscription, source, OS, and state
- Multiple Entra accounts with isolated Azure CLI profiles and explicit, opt-in scan scope
- Azure Resource Graph discovery and de-duplication across accounts
- Azure VM correlation with network interfaces, addresses, virtual networks, peering, and Bastion
- Azure Arc and Azure Local discovery with OpenSSH readiness
- Direct RDP/SSH, Bastion tunnel and native client, and Arc SSH/RDP routing
- Local accounts pinned per machine, with a one-time picker for anything unpinned
- Passwords in Windows Credential Manager; SQLCipher metadata with a DPAPI-protected key
- Hidden helper processes, managed sessions, and session-aware exit protection
- Notification-area lifecycle, background refresh, themes, favourites, search, route override
- In-app update check, verification, and install
- Explicit preview and approval before any Arc OpenSSH deployment

## Quick start

Requirements: Windows 10 19041 or newer (x64), Azure CLI, `mstsc.exe`, and the Windows OpenSSH
client. The app can install or update its required Azure CLI extensions from **Settings**.

```powershell
pwsh ./scripts/Build.ps1
pwsh ./scripts/Run.ps1 -Configuration Release
```

For a self-contained ZIP and MSI:

```powershell
pwsh ./scripts/Package.ps1 -Version 0.2.0 -Installer
```

Then follow [the quickstart](docs/guide/quickstart.md) and
[the interface tour](docs/guide/interface.md).

## Built for laptops and RDP

The window shrinks to 320 x 300 and uses compact density with flat, square, opaque surfaces. That
is deliberate — it fits a laptop screen, fits inside a windowed RDP session, and compresses cleanly
over a remote desktop connection.

## Authentication reality

Stagecoach removes repeated prompts where Windows and the connection protocol safely permit it. It
does not bypass Conditional Access, MFA, PIM, expired Azure sessions, Remote Desktop policy, SSH
host-key verification, or a machine that rejects the account you chose. Entra sign-in and in-guest
sign-in are intentionally separate.

## Security boundary

Stagecoach runs as the signed-in Windows user. Azure token state lives in a separate
Windows-encrypted Azure CLI profile per account. Metadata is encrypted with SQLCipher using a
DPAPI-protected key. Local account passwords are stored only in Windows Credential Manager;
temporary `TERMSRV` entries are removed when the managed session ends. No credential value is
logged or written to the metadata database.

Azure discovery is read-only. The only Azure mutation is the optional Arc OpenSSH extension
deployment, which requires a visible two-step confirmation.

Updates are accepted only from the signed Stagecoach release repository and are hash-verified twice
before Windows Installer runs — see [updating Stagecoach](docs/guide/updates.md).

See [required Azure access](docs/guide/quickstart.md#requirements),
[architecture](docs/guide/architecture.md), and
[the accepted design](pmo/plans/stagecoach-design.md).

## License

MIT — see [LICENSE](LICENSE).
