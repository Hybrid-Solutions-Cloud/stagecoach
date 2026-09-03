# Download Stagecoach

Windows 10 19041 or later, x64.

## Installer (recommended)

<div class="download-row">
  <a class="download-button" href="https://github.com/Hybrid-Solutions-Cloud/stagecoach/releases/latest/download/Stagecoach-0.1.0-win-x64.msi">
    Download the MSI
  </a>
  <a class="download-alt" href="https://github.com/Hybrid-Solutions-Cloud/stagecoach/releases/latest">
    All releases and checksums →
  </a>
</div>

Run the MSI, then launch **Stagecoach** from the Start menu. It installs per machine, creates a
Start menu shortcut, and upgrades cleanly over a previous version.

## Portable ZIP

<div class="download-row">
  <a class="download-button" href="https://github.com/Hybrid-Solutions-Cloud/stagecoach/releases/latest/download/Stagecoach-0.1.0-win-x64.zip">
    Download the ZIP
  </a>
</div>

Self-contained — no .NET runtime required. Unpack it anywhere and run `Stagecoach.App.exe`.

## Verify what you downloaded

Every release publishes a `.sha256` sidecar next to each file. Check it before running:

```powershell
Get-FileHash .\Stagecoach-0.1.0-win-x64.msi -Algorithm SHA256
# compare against Stagecoach-0.1.0-win-x64.msi.sha256
```

::: warning Not code signed yet
Releases are not yet Authenticode signed, so SmartScreen will warn on first run. Verify the
SHA-256 above before you continue past it. Code signing is tracked on the
[roadmap](./about/roadmap.md).
:::

## Before you start

| Requirement | Notes |
|---|---|
| Azure CLI | [Install for Windows](https://learn.microsoft.com/cli/azure/install-azure-cli-windows) |
| Windows OpenSSH client | `Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0` |
| Remote Desktop Connection | `mstsc.exe`, already present on supported Windows builds |
| Azure RBAC | Stagecoach uses the access you already have; it does not grant any |

Stagecoach can install its own required Azure CLI extensions from **Settings → Workstation
readiness** after first launch.

Then follow the [quickstart](./guide/quickstart.md).

## Updating

Once installed, Stagecoach updates itself: **Settings → Software updates**. It only accepts
releases from the signed Stagecoach release repository and verifies the installer against its
authenticated digest before Windows Installer runs. See
[updating Stagecoach](./guide/updates.md).

## Build it yourself

```powershell
git clone https://github.com/Hybrid-Solutions-Cloud/stagecoach.git
cd stagecoach
pwsh ./scripts/Package.ps1 -Version 0.1.0 -Installer
```

The ZIP and its checksum land in `artifacts/`; the MSI lands in `installer/bin/Release/`. See the
[build scripts reference](./reference/scripts.md).

<style scoped>
.download-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
  margin: 20px 0;
}
.download-button {
  display: inline-block;
  padding: 12px 24px;
  border-radius: 6px;
  background: var(--vp-c-brand-1);
  color: var(--vp-c-white);
  font-weight: 600;
  text-decoration: none;
  transition: background-color 0.2s;
}
.download-button:hover {
  background: var(--vp-c-brand-2);
  color: var(--vp-c-white);
}
.download-alt {
  font-weight: 500;
}
</style>
