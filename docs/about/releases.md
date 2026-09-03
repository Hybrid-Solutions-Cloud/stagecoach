# Release notes

Every release is published on the
[GitHub releases page](https://github.com/Hybrid-Solutions-Cloud/stagecoach/releases) with the MSI,
a self-contained ZIP, and a SHA-256 sidecar.

Stagecoach can also update itself — **Settings → Software updates**. See
[Updating Stagecoach](../guide/updates.md) for what is verified before an installer is allowed to
run.

## 0.1.0

First native Windows release. See the [changelog](./changelog.md).

Live Azure-dependent paths — Entra sign-in, subscription discovery, Bastion, Arc, Azure Local,
target credentials, Conditional Access, and OpenSSH deployment — require representative authorized
Azure resources and are not proven by a local build.
