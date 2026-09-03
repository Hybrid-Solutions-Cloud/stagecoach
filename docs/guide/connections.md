# Connection routes

Stagecoach correlates each machine against the network topology your account can read, then picks
the best route it can actually use. A route is a verdict, not a guess.

## Routes

| Route | Used for |
|---|---|
| **Bastion tunnel RDP** | Windows Azure VM reachable through an Azure Bastion host with native client tunneling |
| **Bastion RDP** | Windows Azure VM using the Bastion native-client Entra flow |
| **Bastion SSH** | Linux Azure VM through Bastion |
| **Direct RDP** | Windows Azure VM with a reachable address and direct access explicitly enabled |
| **Direct SSH** | Linux Azure VM with a reachable address |
| **Arc RDP** | Windows Azure Arc or Azure Local machine, Remote Desktop relayed over Arc SSH |
| **Arc SSH** | Linux or Windows Arc machine, SSH over the Arc Hybrid Connectivity endpoint |

Override the choice per machine with **Edit → Route override**.

## Readiness

Every route carries one of these, shown in the machine list:

| Shown | Meaning |
|---|---|
| **Ready** | The route can be used now |
| **Sign-in** | Reachable, but Entra or Conditional Access interaction is required |
| **Prereq** | Something is missing, such as the OpenSSH extension on an Arc machine |
| **Offline** | The VM is deallocated or the Arc agent is disconnected |
| **Denied** | Your account cannot read a resource the route depends on |
| **Unsupported** | No supported route exists for this machine |

Powered-off VMs and disconnected Arc agents stay in the list with an explanation rather than
disappearing.

## Bastion correlation

Bastion eligibility is derived from topology, not assumed:

1. VM network interface to its subnet and virtual network.
2. Bastion host to the virtual network holding its `AzureBastionSubnet`.
3. Same-virtual-network reachability.
4. Virtual network and global peering reachability.
5. Bastion SKU and native-client / tunneling configuration.
6. Read permissions on the VM, interface, virtual network, and Bastion host.

Any step that cannot be confirmed produces **Prereq** or **Denied** with the reason, rather than a
route that fails at launch.

## What happens on Connect

1. The selected Entra session is validated silently.
2. The best ready route is selected, or your override is used.
3. The pinned local account is resolved — or you are asked once, and it is remembered.
4. The password is read from Windows Credential Manager, at launch only.
5. A temporary `TERMSRV/<endpoint>` credential is staged so Remote Desktop does not prompt.
6. The Azure CLI helper starts **hidden**, in that identity's isolated environment.
7. `mstsc.exe` or OpenSSH starts.
8. Both the helper and the local port are monitored.
9. The temporary credential and helper state are removed when the session ends.
10. Non-secret session metadata is recorded.

No console window is ever shown. Credentials never appear in arguments, files, the database, or
logs.

## Arc: one account for both hops

An Arc RDP session needs SSH first and Remote Desktop second. Both use the **same** pinned local
account. Stagecoach never asks you twice, and never asks you to type a local administrator account
for an Arc machine.

## What one click cannot do

Stagecoach removes redundant prompts. It cannot remove:

- Conditional Access or MFA challenges
- expired or revoked Azure sessions needing re-authentication
- the Entra prompts the Microsoft native-client Bastion flow requires
- first-use host-key trust decisions
- a target password changed outside the account you stored

These surface as a clear message in the error banner, not a hidden process waiting forever.

## Arc prerequisites

When an Arc machine is missing OpenSSH, **Prepare Arc OpenSSH** shows the exact machine, account,
subscription, and operations before anything happens. Discovery is read-only; this is the only
Azure write Stagecoach performs, and only after you explicitly approve it.
