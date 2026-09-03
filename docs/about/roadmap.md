# Roadmap

## Delivered

- Native Windows shell on Avalonia and .NET 10, laid out for laptops and RDP sessions
- Machine list as the landing screen, with tenant, subscription, source, OS, and state filters
- Multiple Entra accounts with isolated Azure CLI profiles and explicit scan scope
- Separate **Refresh available scope** and **Rescan machines** actions
- Azure Resource Graph discovery and Bastion topology correlation
- Local accounts in Windows Credential Manager, pinned per machine
- One local account for both Arc hops, so Arc never prompts twice
- Hidden helper processes, temporary `TERMSRV` credential staging, session registry
- Notification-area lifecycle with session-aware exit protection
- Governed Arc OpenSSH remediation preview
- In-app update check, verification, and install
- WiX MSI and self-contained ZIP packaging

## Next

- Live validation against representative authorized Azure estates
- Release pipeline publishing MSI, SHA-256 sidecar, and Sigstore bundle
- Code signing and public release-channel ownership
- Support bundle export

## Not planned

- Hosted or multi-user deployment
- Local administrator password management or rotation
- General Azure inventory and governance reporting
