# Release notes

Every release is published on the
[GitHub releases page](https://github.com/Hybrid-Solutions-Cloud/stagecoach/releases) with the MSI,
a self-contained ZIP, and SHA-256 sidecars. See [Download](../download.md) for direct links.

Stagecoach can also update itself — **Settings → Software updates**. See
[Updating Stagecoach](../guide/updates.md) for what is verified before an installer is allowed to
run.

## 0.6.1

**Stagecoach no longer has a passphrase.** Vault Prospector never asked for one and neither should
this. The local database is encrypted with a key that Windows protects for the account that owns the
installation, so it cannot be read by another Windows user or moved to another machine; opening the
application is gated by Windows Hello, by a Windows credential prompt where Hello cannot prompt — as
it never can inside a remote session — or by signing in to the owning Entra account.

If you already set a passphrase, you are asked for it once so the key can be re-protected without
it. After that it is gone.

See the [changelog](./changelog.md) for the full list.

## 0.6.0

An owner account for the installation, chosen at first run and kept separate from the Entra accounts
used to discover machines. Quick Connect rebuilt as a wizard that uses nothing the application has
stored. Machines that accept Entra sign-in are marked as such instead of asking for a local account.

## 0.5.0

The Activity page and its audit log, export and import of settings, and Quick Connect.

## 0.4.1

**Azure Resource Graph discovery had never worked in any release** — fixed here. Excluding a tenant
now also excludes its subscriptions from scanning.

## 0.2.0

The interface rebuild. The application now opens directly onto a filterable machine list, local
accounts are pinned per machine so connecting never prompts, Arc uses one account for both hops,
the app keeps running in the notification area while sessions are live, and it can update itself.
The window fits a laptop screen and a windowed RDP session.

See the [changelog](./changelog.md) for the full list.

## 0.1.0

First native Windows release.

## Validation status

Live Azure-dependent paths — Entra sign-in, subscription discovery, Bastion correlation, Arc,
Azure Local, target credentials, Conditional Access, and OpenSSH deployment — require
representative authorized Azure resources and are not proven by a local build.
