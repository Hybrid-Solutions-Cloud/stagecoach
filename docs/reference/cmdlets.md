# Azure CLI integration reference

Stagecoach is a desktop app and does not expose a PowerShell module or localhost API. Its supported automation surface is the repository build/package scripts.

The app invokes these Azure CLI command families through argument-safe process APIs and an identity-specific `AZURE_CONFIG_DIR`:

- `az login`
- `az account list`
- `az graph query`
- `az network bastion tunnel|rdp|ssh`
- `az ssh arc`
- `az connectedmachine extension create`

Connection helpers inherit `AZURE_EXTENSION_DIR=%LOCALAPPDATA%\Stagecoach\azure-cli-extensions`. Sensitive values are not included in Azure CLI arguments.
