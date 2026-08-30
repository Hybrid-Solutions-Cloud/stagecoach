# Decisions

- Native Windows Avalonia/.NET 10 application; no browser UI or localhost server.
- Per-Entra-identity isolated `AZURE_CONFIG_DIR` because supported Bastion and Arc connection paths are Azure CLI based.
- Entra identities and target/relay connection identities are separate models.
- SQLCipher metadata database with a DPAPI CurrentUser-protected key; passwords remain in Windows Credential Manager.
- New tenants and subscriptions are excluded until explicitly enabled.
- Discovery is read-only. WindowsOpenSSH Arc deployment is the only v1 Azure write and requires a two-step UI confirmation.
- Managed helper lifetime and temporary RDP credential cleanup are part of connection correctness.
