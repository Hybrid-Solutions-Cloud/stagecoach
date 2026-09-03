# Release notes

Every release is published on the
[GitHub releases page](https://github.com/Hybrid-Solutions-Cloud/stagecoach/releases) with the MSI,
a self-contained ZIP, and SHA-256 sidecars. See [Download](../download.md) for direct links.

Stagecoach can also update itself — **Settings → Software updates**. See
[Updating Stagecoach](../guide/updates.md) for what is verified before an installer is allowed to
run.

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
