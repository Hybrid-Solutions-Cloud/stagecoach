# Repo intent — Stagecoach

**One identity hub. Every reachable machine. One click.**

## Product

Stagecoach is an installable Windows desktop application for experienced administrators who move
between Azure estates. An operator connects one or more Microsoft Entra identities, explicitly
selects the tenants and subscriptions that each identity may scan, and receives one searchable
estate of Azure virtual machines, Azure Arc-enabled servers, and Azure Local virtual machines.

Selecting a machine launches its best supported RDP or SSH route. Stagecoach handles the Azure
identity context, Bastion or Arc relay, target connection identity, temporary credential staging,
background helper process, and cleanup. The normal path is one click after onboarding. Microsoft
Entra Conditional Access, MFA, expired sessions, and platform limitations can still require user
interaction; Stagecoach must explain those cases rather than imply that it bypasses them.

## Authoritative shape

- **Desktop:** Avalonia on .NET 10 LTS, packaged for Windows x64.
- **Azure identities:** one isolated Azure CLI configuration directory per connected identity.
  Azure CLI uses Windows Web Account Manager and an encrypted MSAL cache. Stagecoach never reuses
  or changes the operator's default `~/.azure` profile.
- **Scope:** tenants and subscriptions are discovered per identity and must be explicitly enabled.
- **Discovery:** Azure Resource Graph correlates VMs, Arc machines, NICs, IPs, VM extensions,
  virtual networks, peerings, Bastion hosts, and Arc connectivity prerequisites.
- **Connection identities:** domain, local, and Entra target accounts are separate from Azure
  identities. Usernames and mappings are local metadata; passwords are stored only in Windows
  Credential Manager or resolved just in time from an approved provider.
- **Connections:** Azure CLI extensions, OpenSSH, and MSTSC are orchestrated as managed child
  processes. Bastion and Arc helpers are kept in the background and cleaned up with the session.
- **State:** non-secret metadata is cached locally for instant startup, search, favorites, recents,
  identity scope, mappings, settings, and diagnostics.
- **Lifecycle:** minimize/close-to-notification-area, background refresh, live sessions, and
  configurable theme/accent behavior.

## First-run experience

1. Validate Windows, Azure CLI, OpenSSH, MSTSC, and required CLI extensions.
2. Offer **Use my Windows account** (Azure CLI WAM) or **Add another Entra account**.
3. Enumerate the connected account's tenants and subscriptions.
4. Require the operator to select scan scope.
5. Scan and open the estate screen.
6. Prompt to create connection identity mappings only when a discovered environment requires one.

## Security boundaries

- No passwords, tokens, tenant IDs, subscription IDs, or live resource identifiers are committed.
- No target password is stored in SQLite, JSON, logs, command lines, process arguments, or an RDP
  file. Windows Credential Manager is the default secret store.
- Azure CLI profiles are isolated under the current user's local application-data directory.
- Diagnostics contain stable local correlation IDs and redacted error categories, not credentials
  or access tokens.
- Discovery is read-only. Installing Arc/OpenSSH/connectivity prerequisites is a governed Azure
  write: Stagecoach shows a preview and requires explicit confirmation for the exact target.
- A connection never silently falls back to a different Azure or target identity.

## Non-goals

- Not a hosted service or shared credential broker.
- Not a general Azure inventory/governance platform.
- Not an authorization bypass; operators must already hold required Azure RBAC and in-guest rights.
- Not a replacement for Azure Bastion, Azure Arc, Azure CLI, OpenSSH, MSTSC, LAPS, or Key Vault.
- Not a PowerShell/Pode/browser application. The earlier web design is superseded.

## Authority

The accepted native-Windows design is `pmo/plans/stagecoach-design.md`; delivery order and exit
criteria are in `pmo/plans/stagecoach-implementation-plan.md`.

## Published site

<https://labs.hybridsolutions.cloud/stagecoach/> — documentation and downloads. The domain is a
Cloudflare Worker proxying the organisation's GitHub Pages origin with the path preserved, so this
repo remains a Pages project site with `base: '/stagecoach/'` and carries no `CNAME` file.

## Status

The native-Windows implementation is in release validation. Previous WPF and PowerShell/web
prototypes are not accepted release evidence.
