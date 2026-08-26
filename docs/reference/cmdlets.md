# PowerShell Cmdlet Reference

The `AzureStagecoach` module provides standalone PowerShell 7 cmdlets that can be invoked interactively or in automated scripts without the web interface.

---

## `Start-Stagecoach`

Starts the local Stagecoach web server and opens the dashboard in your default browser.

```powershell
Start-Stagecoach [-Port <int>] [-NoBrowser]
```

### Parameters
- `-Port`: The local TCP port to bind (default: `8085`).
- `-NoBrowser`: Prevents automatically opening the browser upon server start.

---

## `Get-StagecoachInventory`

Queries Azure Resource Graph across accessible subscriptions for Azure VMs and Arc servers, parsing OS, power state, and domain/workgroup identity.

```powershell
Get-StagecoachInventory [-SubscriptionId <string>]
```

### Outputs
- Array of `StagecoachTarget` objects containing `Id`, `Name`, `Kind`, `OsName`, `DomainName`, `DomainType`, and `PowerState`.

---

## `Get-StagecoachCredential`

Resolves credentials for a given target across Entra LAPS, Active Directory Domain secrets, and Key Vault conventions.

```powershell
Get-StagecoachCredential -Target <StagecoachTarget> [-VaultName <string>]
```

### Outputs
- `PSCustomObject` containing `Source`, `Username`, and `Password`.

---

## `Connect-StagecoachVM`

Initiates an RDP or SSH connection to an Azure VM or Arc-enabled server using the optimal Azure CLI command.

```powershell
Connect-StagecoachVM -Target <StagecoachTarget> [-LocalUser <string>] [-Rdp <bool>]
```

### Outputs
- `StagecoachSession` object with `SessionId`, `Method`, `HelperProcessId`, and `State`.

