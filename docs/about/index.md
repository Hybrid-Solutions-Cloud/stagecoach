# About Stagecoach

Stagecoach is an installable native Windows application for administrators who move between Azure
estates. It combines the machines visible to one or more Microsoft Entra accounts into a single
searchable list and turns each one into a deliberate, repeatable RDP or SSH action.

It answers four questions without making you rebuild the context yourself:

1. Which Azure identity can see and connect to this machine?
2. Which route reaches it — direct, Azure Bastion, Azure Arc, or Azure Local through Arc?
3. Which account should be used inside the machine?
4. Is the route ready now, and if not, exactly what is missing?

## What it is not

- Not a hosted service or a shared credential broker.
- Not a general Azure inventory or governance platform.
- Not an authorization bypass. You must already hold the Azure RBAC and in-guest rights.
- Not a replacement for Azure Bastion, Azure Arc, Azure CLI, OpenSSH, Remote Desktop, or Key Vault.
- Not a manager or rotator of local administrator passwords.

## Licence

MIT. Copyright © 2026 Kristopher Turner / Hybrid Cloud Solutions.
