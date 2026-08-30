# Quickstart

## Install and open

Install the MSI, extract the self-contained ZIP, or run a repository build with `pwsh ./scripts/Run.ps1 -Configuration Release`. Stagecoach is a native desktop app; it does not start a local web server.

On first launch, open **Settings** and review workstation readiness. **Install / update local CLI extensions** installs the `resource-graph`, `ssh`, and `connectedmachine` Azure CLI extensions in Stagecoach's shared extension directory. Bastion commands ship with the current Azure CLI and are checked separately.

## Add an Entra identity

1. Open **Identities & scope**.
2. Enter an optional friendly name.
3. Choose **Use my Windows account / choose account** for Windows Web Account Manager, or **Use device code instead** when policy or an RDP session makes that flow preferable.
4. Complete Microsoft sign-in.
5. Review the discovered tenants and subscriptions. Include only the scope Stagecoach should scan.
6. Repeat for every Entra identity you operate.

Each identity has its own `AZURE_CONFIG_DIR`, so account selection is deterministic and one account cannot silently replace another.

## Add target accounts

Open **Connection identities** and create the accounts used inside servers—for example `CORP\admin`, `user@corp.example.com`, or `.\localadmin`. A password is optional for prompt-only, Entra, or SSH-key profiles. Passwords are written to Windows Credential Manager, not the Stagecoach database.

Map each identity to one or more scopes. Specific mappings win over broad mappings:

1. Machine
2. Tag (`key=value`)
3. Domain
4. Resource group
5. Subscription
6. Tenant

For Arc RDP, a mapping can be marked **Arc SSH relay identity**. This lets the SSH tunnel use a different account from the Windows account passed to Remote Desktop.

## Discover and connect

Choose **Sync estate**. The Estate tab merges machines discovered through every enabled identity while retaining every valid access path. Select a route in the row when you need to override the preferred route, then choose **Connect**.

If a Windows Arc or Azure Local machine lacks WindowsOpenSSH, select it and choose **Prepare Arc**. Review the exact extension deployment under **Settings**, then explicitly apply or cancel it. Sync again after Azure reports completion.

## Required access

This tool is intended for administrators who already hold the necessary access. Stagecoach does not grant roles or activate PIM.

- Azure Resource Graph read access to each selected subscription
- Reader access to inventory resources, NICs, VNets, peerings, Bastion, Arc machines, and extensions
- Bastion Reader and Virtual Machine login permissions appropriate to the selected authentication method
- `Microsoft.HybridConnectivity/endpoints/connect/action` and the documented Arc SSH permissions for Arc relay
- Contributor or equivalent extension-write rights only when deploying WindowsOpenSSH
- A valid target-machine account with Remote Desktop or SSH logon rights

Conditional Access, MFA, PIM, network policy, server policy, and endpoint protection remain authoritative.
