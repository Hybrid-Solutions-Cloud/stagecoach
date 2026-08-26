# Release Notes

---

## Stagecoach v0.1.0 (Initial Release)

**Release Date:** August 26, 2026

We are pleased to announce the initial release of **Stagecoach**, a local-first launcher for Azure VMs, Azure Arc-enabled servers, and Bastion-protected machines.

### Highlights
- **1-Click RDP/SSH:** Launch Remote Desktop to Azure Bastion, Arc SSH relays, or direct VMs with a single click.
- **Active Directory & Workgroup Detection:** Automatic classification of on-premises and hybrid servers from Azure Resource Graph.
- **Smart Credential Resolver:** Seamless resolution of credentials across Entra LAPS, AD Domain Accounts, and Azure Key Vault (`kv-hcs-vault-01`).
- **Zero Local Footprint:** No secrets saved to disk, no Node.js compilation required, and zero cloud servers to maintain.
