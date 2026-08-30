# About Stagecoach

Stagecoach is a native Windows connection hub for experienced Azure and hybrid administrators. It replaces per-target scripts and repeated resource-ID copying with a profile-aware estate view.

The application keeps each Entra account isolated, lets the operator opt specific tenants and subscriptions into discovery, correlates the routes available to each machine, and launches the correct native RDP or SSH flow. Azure VM, Bastion, Azure Arc, and Azure Local targets appear in the same interface without pretending their authentication requirements are identical.

Stagecoach is local-first and single-user. It has no hosted control plane and no shared service principal. Azure permissions and target logon rights always belong to the current operator.

- [Quickstart](../guide/quickstart.md)
- [Architecture](../guide/architecture.md)
- [Accepted design](https://github.com/Hybrid-Solutions-Cloud/stagecoach/blob/main/pmo/plans/stagecoach-design.md)
- [Source](https://github.com/Hybrid-Solutions-Cloud/stagecoach)
