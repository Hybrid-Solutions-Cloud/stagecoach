# Architecture

Stagecoach is a Windows-only Avalonia application on .NET 10. It uses no localhost server, browser runtime, service principal, or shared cloud database.

```text
Avalonia UI
  ├─ identity and scope management
  ├─ merged estate, route selection, sessions
  └─ credential mappings, settings, remediation confirmation
          │
          ▼
Core contracts and models
          │
          ▼
Infrastructure
  ├─ isolated Azure CLI profiles (WAM/device code)
  ├─ Azure Resource Graph discovery and correlation
  ├─ SQLCipher metadata + DPAPI database key
  ├─ Windows Credential Manager
  └─ managed az / mstsc / ssh processes
          │
          ▼
Azure Resource Manager, Resource Graph, Bastion, Arc relay
```

## Identity model

An Entra identity owns an isolated Azure CLI configuration directory. Tenant and subscription scope is stored separately and newly discovered scope requires review. A machine may have access paths from multiple identities; Stagecoach never treats one process-wide Azure session as authoritative.

Connection identities are different objects. They represent accounts inside target operating systems and may be mapped by machine, tag, domain, resource group, subscription, or tenant. Arc relay and target desktop identities can be different.

## Discovery model

One bounded Resource Graph query retrieves VMs, Arc machines, Azure Local VM instances, extensions, NICs, public IPs, VNets/peerings, and Bastion hosts. The correlator de-duplicates Azure Local parent/child resources and calculates candidate routes with an explicit readiness state and reason.

## Process boundary

All launch arguments are passed through `ProcessStartInfo.ArgumentList`; no target-derived command text is evaluated by a shell. Bastion and Arc helpers are tracked as managed sessions. RDP credentials are staged as session-persistent Windows credentials and removed when the client ends. SSH password relay uses the small `Stagecoach.AskPass` helper, which resolves one Windows Credential Manager profile at invocation time.

## Local files

Per-user state is under `%LOCALAPPDATA%\Stagecoach`:

- `stagecoach.db`: SQLCipher-encrypted inventory, scope, mappings, favorites, and recents
- `stagecoach.db.key`: DPAPI CurrentUser-protected database key
- `settings.json`: non-secret appearance and refresh settings
- `identities\<id>\azure`: isolated Azure CLI token/config state
- `azure-cli-extensions`: shared CLI extension installation
- `sessions`: short-lived `.rdp` files with endpoint and username only

Resolved passwords and Azure tokens are never stored in the database or settings file.
