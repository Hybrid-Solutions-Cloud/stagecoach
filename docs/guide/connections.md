# Connection Routes & Targets

Stagecoach automatically routes remote sessions through the optimal connection method for each Azure-connected target.

---

## 1. Azure Bastion Native Client

For Azure virtual machines in VNets protected by Azure Bastion:

- **Command Used:** `az network bastion rdp --name <Bastion> --resource-group <RG> --target-resource-id <VMId>`
- **Prerequisites:** Bastion Standard SKU or higher with `enableTunneling=true`.
- **User Experience:** Spawns `mstsc.exe` tunneling through the Bastion gateway without opening public IPs or inbound ports on the target VM.

---

## 2. Azure Arc-Enabled Servers & Azure Local VMs

For on-premises Windows Servers, VMware VMs, Hyper-V, and bare-metal nodes connected via Azure Arc:

- **Command Used:** `az ssh arc --resource-group <RG> --name <MachineName> --local-user <User> --rdp`
- **How It Works:** Establishes an outbound SSH relay through the Azure Arc agent (`himds`) and Hybrid Connectivity endpoint, then launches MSTSC over the tunnel.
- **Active Directory Domain Accounts:** Stagecoach formats the `--local-user` argument as `"DOMAIN\Username"`, authenticating seamlessly against on-premises Active Directory domain controllers.

---

## 3. Direct-Reachable Azure VMs

For VMs accessible via ExpressRoute, site-to-site VPN, or public IP:

- **Command Used:** `mstsc /v:<TargetIP>` or `az ssh vm --rdp`
- **User Experience:** Directly launches Remote Desktop to the VM's private or public IP address.

