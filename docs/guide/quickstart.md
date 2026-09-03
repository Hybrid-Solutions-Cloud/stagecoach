# Quickstart

Stagecoach is an installable native Windows application. Install it, connect one Microsoft Entra
account, add the local account you use inside your machines, and connect.

## Requirements

- Windows 10 19041 or later, x64
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli-windows)
- Windows OpenSSH client (`Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0`)
- Remote Desktop Connection (`mstsc.exe`, present on every supported Windows build)
- Azure RBAC that already lets you read the resources and reach the machines. Stagecoach does not
  grant access; it uses the access you have.

## 1. Install

Run `Stagecoach-<version>-win-x64.msi`, or unpack the self-contained ZIP and run
`Stagecoach.App.exe`. Both are published on the
[releases page](https://github.com/Hybrid-Solutions-Cloud/stagecoach/releases).

## 2. Connect an Entra account

Open **Connect identities** and choose **Use my Windows account**. Web Account Manager offers the
account you are already signed in with. **Use a device code instead** covers sessions where the
broker cannot show interactive UI.

Conditional Access and MFA prompts still appear. Stagecoach does not suppress them and does not
claim to.

## 3. Include the scope you want scanned

Stagecoach lists the tenants and subscriptions the account can see. Nothing is scanned until you
include it — use **Include / exclude** on the ones you want, then **Rescan machines**.

New tenants and subscriptions that appear later are marked for review rather than silently added.

## 4. Add a local account

Open **Local accounts** and add the account you use inside the machines:

| Field | Example |
|---|---|
| Display name | `Prod local admin` |
| Username | `svcadmin` for a local account, `CORP\svcadmin` for a domain account |
| Password | Stored in Windows Credential Manager |

For an Azure VM this is the local administrator created when the VM was provisioned — the
`adminUsername` and `adminPassword` from its deployment.

## 5. Pin it, then connect

On **Machines**, select a machine and choose **Edit**, then pick the local account under
**Pinned local account** and save. That machine now connects on the first click without asking.

If you skip pinning, the first **Connect** asks which stored account to use and remembers the
answer. Either way you never type a credential at connect time.

Click **Connect**. The Azure CLI helper runs hidden — no console window appears — and the Remote
Desktop or SSH session opens. Progress shows in the status bar at the bottom of the window.

## 6. Keep it running

Minimizing sends Stagecoach to the notification area so live sessions keep running. See
[the interface](./interface.md) for the rest.
