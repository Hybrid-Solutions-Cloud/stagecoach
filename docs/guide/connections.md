# Connection routes

| Target | Route | Behavior |
|---|---|---|
| Windows Azure VM with public IP | Direct RDP | Stages mapped Windows credential and starts `mstsc.exe` |
| Linux Azure VM with public IP | Direct SSH | Starts Windows Terminal or `cmd.exe` with OpenSSH |
| Windows Azure VM behind Standard+ Bastion | Bastion tunnel RDP | Starts an Azure CLI tunnel, waits for the loopback port, then starts RDP |
| Azure VM with Entra-native Bastion flow | Bastion native RDP | Starts the Bastion native client; Entra/MFA interaction can still be required |
| Windows Arc/Azure Local | Arc RDP | Runs `az ssh arc --rdp`, with separate relay and target account mappings supported |
| Linux Arc/Azure Local | Arc SSH | Runs `az ssh arc` with the owning Entra profile |

Stagecoach prefers a ready Bastion tunnel, then Arc RDP, then interactive native routes, then direct routes. The operator can choose another discovered route in the Estate grid.

One click means Stagecoach performs routing, profile selection, credential lookup, helper startup, and client launch. It does not suppress security controls that demand interaction.

## Arc readiness

Arc relay requires a connected Arc agent, supported agent version, Hybrid Connectivity endpoint/service configuration, a running target SSH service, and client-side Azure CLI/OpenSSH prerequisites. WindowsOpenSSH extension installation is an Azure write and therefore appears only behind the **Prepare Arc** preview and explicit **Apply approved change** action.
