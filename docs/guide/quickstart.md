# Quickstart Guide

Get up and running with Stagecoach in less than 2 minutes.

---

## Prerequisites

Before running Stagecoach, ensure your local workstation has:

1. **PowerShell 7+** (`pwsh`)
2. **Azure CLI** (`az`) installed and authenticated:
   ```powershell
   az login
   ```
3. **Azure CLI Extensions:**
   ```powershell
   az extension add --name ssh
   az extension add --name bastion
   ```

---

## Installation & Launch

### 1. Import the Module
Clone the repository and import the module into your PowerShell session:

```powershell
Import-Module ./src/AzureStagecoach/AzureStagecoach.psd1 -Force
```

### 2. Launch the Stagecoach Dashboard
Start the local server and launch the web interface in your default browser:

```powershell
Start-Stagecoach
```

This launches the local listener at `http://127.0.0.1:8085/` and opens the single-file React interface.

---

## Connecting to Your First Machine

1. **Scan Your Estate:** Click **"Scan Estate"** in the top navigation bar. Stagecoach queries Azure Resource Graph across all accessible subscriptions.
2. **Review Auto-Categorized Machines:**
   - **Domain-Joined Arc Servers:** Highlighted with a blue badge (`CORP.CONTOSO.COM`) and pre-filled with the domain admin account.
   - **Workgroup Arc Servers:** Highlighted with a yellow badge (`Workgroup`) and matched against per-VM Key Vault secrets.
   - **Bastion Azure VMs:** Highlighted with a cyan badge.
3. **Launch RDP:** Click **Connect** on any machine row, verify the target username in the drawer, and click **"Launch 1-Click RDP"**. Native Windows Remote Desktop (`mstsc.exe`) will open automatically!

