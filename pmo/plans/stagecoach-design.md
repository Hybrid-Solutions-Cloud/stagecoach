# Stagecoach — one-click RDP/SSH launcher for Azure, Bastion, and Arc VMs

> Canonical copy. Originally drafted in `azure-scout` branch `claude/azure-vm-rdp-tool-akli2k`
> (`pmo/plans/azure-stagecoach-design.md`); this repo is now the home of the plan.

**Status:** Accepted — repo live, brand and docs pipeline in place, onboarding in progress
**Repo:** `Hybrid-Solutions-Cloud/stagecoach` (private)
**ADO project:** `HCS - Stagecoach` (created 2026-08-23, Agile process)
**Docs site:** <https://labs.hybridsolutions.cloud/stagecoach/> once Pages is enabled
**Author:** drafted by Claude Code for Kristopher Turner, 2026-08-23
**Work item:** _to be created (AB# pending)_

---

## 1. Problem statement

Daily operations involve RDP into three different kinds of Windows/Linux machines,
each with a different Azure CLI incantation:

- **Azure VMs behind Azure Bastion** — `az network bastion rdp/ssh/tunnel`
- **Azure Arc-enabled servers** (including Azure Local VMs) — `az ssh arc [--rdp]`
- **Plain Azure VMs** (public IP or reachable private IP) — `az ssh vm [--rdp]` / mstsc

Today this is done by hand-run "parachute" scripts per target. The operator signs in
with one Entra ID account that is a member of several Entra tenants, and wants:

1. A simple clickable tool (web-style UI, nothing fancy).
2. Sign in once with an Entra ID account.
3. The tool scans everything that account can reach — across all its tenants — and
   lists every VM that is actually connectable (via Bastion, via Arc SSH, or direct).
4. Click a VM → the right kind of session opens with the current authentication.
5. A sane story for the local administrator password when Entra login isn't possible.

## 2. Research: the connection matrix (verified against Microsoft Learn, Aug 2026)

### 2.1 Azure VM behind Azure Bastion (native client)

Requires **Bastion Standard SKU or higher** with native client support enabled
(`enableTunneling`). The Developer/Basic SKUs do not support CLI/native-client
connections. The client signs in with `az login` first.

| Scenario | Command |
|---|---|
| RDP to Windows VM | `az network bastion rdp --name <bastion> --resource-group <bastion-rg> --target-resource-id <vm-resource-id>` |
| RDP with Entra ID auth/MFA (Win 10 20H2+/Win 11/WS2022+) | same + `--enable-mfa` |
| SSH to Linux VM with Entra ID | `az network bastion ssh --name <bastion> --resource-group <rg> --target-resource-id <vm-id> --auth-type AAD` |
| SSH with key / password | `--auth-type ssh-key --username <u> --ssh-key <path>` or `--auth-type password --username <u>` |
| Generic tunnel (any client, non-Windows workstation, custom ports) | `az network bastion tunnel --name <bastion> --resource-group <rg> --target-resource-id <vm-id> --resource-port 3389 --port <local-port>` then point mstsc/ssh/xfreerdp at `127.0.0.1:<local-port>` |
| IP-based connection (VM in peered VNet, non-Azure target) | replace `--target-resource-id` with `--target-ip-address <ip>` (no Entra auth, no custom ports) |

Notes:
- `az network bastion rdp` launches **MSTSC** and is only available on a Windows
  workstation; on macOS/Linux the `tunnel` command is the fallback.
- The credential prompt accepts local creds **or** Entra credentials; Entra RDP
  requires the client PC to be Entra-joined/registered to the **same** directory
  and the VM to run the **AADLoginForWindows** extension.
- RDP user must be a local admin or in `Remote Desktop Users` on the target.

### 2.2 Azure Arc-enabled server (also Azure Local VMs — no public IP, no VPN, no open inbound ports)

Requires the `ssh` CLI extension (>= 2.0.4), the Arc agent's default connectivity
endpoint enabled, and SSHD running on the target (WS2025 ships OpenSSH by default;
older Windows needs the `WindowsOpenSSH` Arc extension). Traffic rides the Arc
agent's outbound connection — nothing inbound is opened.

| Scenario | Command |
|---|---|
| SSH, local user (password) | `az ssh arc --resource-group <rg> --name <machine> --local-user <user>` |
| SSH, Entra ID cert-based (Linux, AADSSHLoginForLinux ext + VM Login role) | `az ssh arc --resource-group <rg> --name <machine>` |
| **RDP over SSH to a Windows Arc machine** | `az ssh arc --resource-group <rg> --name <machine> --local-user <user> --rdp` |
| PowerShell equivalents | `Enter-AzVM -ResourceGroupName <rg> -Name <machine> -LocalUser <user> [-Rdp]` (Az.Ssh + Az.Ssh.ArcProxy modules) |
| Export OpenSSH config (works with VS Code Remote-SSH, rsync, git) | `az ssh config --resource-group <rg> --name <machine> --local-user <u> --file <path>` |

Notes:
- `--rdp` tunnels 3389 over the Arc SSH channel and launches mstsc — **Windows
  client only**; Linux Arc machines don't support `--rdp`.
- Entra ID SSH needs the **Virtual Machine Administrator Login** or **Virtual
  Machine User Login** Azure role on the machine scope (dataActions — assign at
  RG/subscription level, not per-VM).
- One-time per machine: the Hybrid Connectivity `default` endpoint +
  SSH service configuration (port 22) must exist; the CLI offers to create it at
  runtime (`--yes` to auto-accept).

### 2.3 Plain Azure VM (direct network reachability)

| Scenario | Command |
|---|---|
| SSH, Entra ID cert-based (Linux + AADSSHLoginForLinux) | `az ssh vm --resource-group <rg> --name <vm>` or `az ssh vm --ip <ip>` |
| SSH, local user | `az ssh vm --local-user <u> ...` |
| **RDP over SSH to Windows VM** | `az ssh vm --resource-group <rg> --name <vm> --local-user <u> --rdp` |
| Classic direct RDP (public IP / VPN / ExpressRoute) | `mstsc /v:<ip>` |

`az ssh vm` resolves the VM's IP itself (`--prefer-private-ip` when you have
line-of-sight to the private address). AAD-issued OpenSSH certificates are
currently Linux-only; Windows targets use `--local-user`.

### 2.4 Local administrator password — the options

1. **Windows LAPS backed by Entra ID** (preferred for Entra-joined/Arc-enabled
   Windows machines). Retrieval:
   - `Get-LapsAADPassword -DeviceIds <name-or-id> -IncludePasswords -AsPlainText`
     (LAPS PS module wrapping Microsoft Graph), or
   - Graph API `deviceLocalCredentials` (`GET /directory/deviceLocalCredentials/{deviceId}?$select=credentials`).
   - Permissions: delegated `DeviceLocalCredential.Read.All` + `Device.Read.All`
     (Graph scopes) or a directory role holding
     `microsoft.directory/deviceLocalCredentials/password/read`
     (Cloud Device Administrator, Intune Administrator).
   - Passwords come back Base64-encoded from raw Graph and must be decoded.
2. **Key Vault convention** — HCS already runs `kv-hcs-vault-01` and the
   `vault-prospector` tool. A per-VM secret naming convention
   (e.g. `vm-<name>-localadmin`) lets the tool fetch with the signed-in user's
   RBAC (`Key Vault Secrets User`).
3. **Manual entry** — always available; the CLI prompts anyway. The tool's job is
   then just to get the password onto the clipboard safely (auto-clear ~30 s).
4. Last-resort reset: VMAccess extension (`az vm user update`) for Azure VMs —
   out of scope for v1 (write operation).

**Decision for v1:** pluggable *credential resolver* chain — Entra LAPS → Key
Vault convention → manual prompt. Never persist a password to disk; clipboard
copy with automatic clear; session launch preferred over display.

#### 2.4.1 Key Vault credential design (protected local-admin passwords)

Key Vault is the resolver step that covers machines LAPS doesn't (workgroup
servers, appliances, Arc machines without Entra LAPS) while keeping the
password protected end to end:

- **Where the password lives:** an Azure Key Vault secret, never a Stagecoach
  file. Default convention `vm-<machine-name>-localadmin` in a configured vault
  (HCS default `kv-hcs-vault-01`); a machine can override via a resource tag
  `stagecoach-secret` whose value is the full secret ID
  (`https://<vault>.vault.azure.net/secrets/<name>`), which also supports
  per-tenant or per-customer vaults with zero config.
- **How it's read:** with the *signed-in user's own token* —
  `az keyvault secret show --id <secret-id> --query value -o tsv` — so access
  is governed by RBAC (`Key Vault Secrets User` on the vault or secret) and
  every read lands in the vault's audit log with the operator's identity.
  Stagecoach adds no service principal and no standing access of its own.
- **How it's protected in flight:** held as `SecureString` in backend memory
  only for the seconds needed to stage the session; passed to the spawned
  command's stdin or prompt, or placed on the clipboard with a ~30 s
  auto-clear; redacted from all logs; never rendered in the UI (the drawer
  shows only "Key Vault ✓ · vm-web01-localadmin").
- **Write-back (opt-in):** when the operator types a password manually, the
  drawer offers "Save to Key Vault for next time" — an explicit, confirmed
  write (`az keyvault secret set`), consistent with the confirm-before-write
  posture. Off by default.
- **Freshness:** secret `expires`/`updated` metadata is surfaced as a staleness
  hint in the drawer; LAPS-managed machines prefer LAPS (auto-rotated) over a
  stale KV copy when both exist.

### 2.5 Discovery — what can I reach with this login?

One **Azure Resource Graph** query per tenant covers the whole estate the
signed-in identity can see (ARG is automatically scoped to the caller's RBAC):

```kusto
Resources
| where type =~ 'microsoft.compute/virtualmachines'
    or type =~ 'microsoft.hybridcompute/machines'
| extend kind = iff(type =~ 'microsoft.compute/virtualmachines', 'azure-vm', 'arc'),
         os = coalesce(tostring(properties.storageProfile.osDisk.osType),
                       tostring(properties.osType), tostring(properties.osName)),
         powerState = coalesce(tostring(properties.extended.instanceView.powerState.displayStatus),
                               tostring(properties.status)),
         agentStatus = tostring(properties.status)
| project id, name, resourceGroup, subscriptionId, tenantId, location, kind, os, powerState, tags
```

Supplementary per-tenant queries:
- **Bastions:** `Resources | where type =~ 'microsoft.network/bastionhosts'` —
  project SKU + `enableTunneling` + VNet; join VM NIC → VNet → Bastion VNet
  (including peerings) to decide "reachable via Bastion" and pick the host.
- **NIC/public IP:** to decide direct-connect eligibility.
- **Extensions:** presence of `AADSSHLoginForLinux` / `AADLoginForWindows` /
  `WindowsOpenSSH` to decide Entra-auth eligibility per machine.
- **Tenants:** `GET /tenants` (ARM) enumerates every tenant the account belongs
  to; the tool re-acquires a token per tenant and repeats the sweep. This is
  exactly the multi-tenant model already proven in azure-scout's
  enterprise multi-tenant scans.

Per row, a **capability decision tree** yields the buttons the UI shows:

```
Arc machine?            → SSH (always) · RDP-over-SSH (Windows target + Windows client)
Azure VM + Bastion?     → Bastion RDP (Windows) · Bastion SSH (Linux) · Tunnel (any port)
Azure VM + public IP or
  reachable private IP? → az ssh vm · direct mstsc
Entra extensions found? → offer Entra auth first, else local-user flow
Powered off / agent down → row greyed with the reason
```

## 3. Naming

Existing fleet: **azure-scout**, **hyperv-surveyor**, **vault-prospector**,
**homestead-foundry**, **ranch-hand**, **repo-wrangler**, **azurelocal-ranger /
-surveyor / -beacon / -cartographer / -draftsman**. The house style is a
frontier/western working name, lowercase, often `azure-` prefixed in this org.

**Decision: `stagecoach` (product name "Stagecoach") — operator approved.** The `azure-` prefix was dropped by operator choice; the org context already scopes it.

Original rationale:
The stagecoach is the frontier vehicle that carried passengers safely between
outposts along established secure routes — exactly what this tool does: it
carries your authenticated session to any VM over whichever secure route exists
(Bastion, Arc SSH, direct). It sits naturally beside scout/surveyor/prospector
("scout finds the territory, stagecoach takes you there").

Runners-up, all unclaimed in the org: `azure-outrider` (rides ahead and escorts),
`azure-drawbridge` (bastion imagery, off-theme), `azure-trailhead`.

Tagline: *"One login. Every VM. One click."*

## 4. Architecture

### 4.1 Shape: local-first web app

A browser page cannot spawn `mstsc`/`az`, so the tool is a **small local app**:
a localhost backend that shells out to Azure CLI, plus a single-file React UI.

```
┌────────────────────────────── operator workstation ─────────────────────────────┐
│  Browser (single-file React UI, http://127.0.0.1:<port>)                        │
│    sign-in card → tenant/sub picker → estate grid → connect drawer → sessions   │
│        │  REST + SSE (localhost only, per-launch token)                         │
│  Stagecoach service (PowerShell 7 + Pode)                                       │
│    ├─ auth broker........ drives az login / account & tenant switching          │
│    ├─ discovery engine... per-tenant ARG sweeps → capability decision tree      │
│    ├─ launcher........... spawns az network bastion rdp|ssh|tunnel,             │
│    │                      az ssh arc|vm [--rdp], mstsc — detached processes     │
│    ├─ credential resolver LAPS (Graph) → Key Vault → prompt; clipboard+autoclear│
│    └─ session registry... live tunnels/sessions, ports, teardown                │
│        │ shells out to                                                          │
│  az CLI (+ ssh extension) · OpenSSH · mstsc                                     │
└─────────────────────────────────────────────────────────────────────────────────┘
          │ HTTPS (user's Entra ID tokens via az)
   Azure ARM / Resource Graph / Microsoft Graph / Bastion / Arc relay
```

**Why PowerShell 7 + Pode for the backend:** HCS hard rules mandate PS7 for all
scripting; the whole job of this backend is orchestrating `az`/`mstsc`/OpenSSH
child processes, which PowerShell does natively; and the core can double as a
plain PowerShell module (`Connect-StagecoachVM`, `Get-StagecoachInventory`) that
power users and future tools call without the UI — the azure-scout pattern.

**Frontend is one file, on purpose.** The UI is a single static
`stagecoach.html` — React loaded from vendored UMD bundles shipped alongside it
(with `htm` tagged templates instead of JSX), served as-is by Pode. No Vite, no
webpack, no `node_modules`, no build step, no Node dependency at any point:
edit the file, refresh the browser. Simple and interactive is the whole spec;
if the UI ever genuinely outgrows one file, introducing a build pipeline is a
deliberate later decision, not a day-one tax.

**The launch chain always goes through PowerShell.** A click in the browser
never runs a command itself — it calls the local API, and the backend launches
a `pwsh` process running the connection cmdlet
(`pwsh -NoProfile -Command Connect-StagecoachVM ...`). That PowerShell process
is the direct descendant of today's parachute scripts: it stages credentials,
invokes the right `az` command, and `az` opens mstsc or the tunnel. The UI is
just the finger that pulls the trigger; PowerShell remains the thing that fires.

Packaging path later: PS module on PSGallery (`Install-Module AzureStagecoach;
Start-Stagecoach` opens the browser), optional Tauri/Electron shell if a real
desktop app is ever wanted. Not v1.

### 4.2 Authentication flow

1. UI "Sign in" → backend runs `az login` (interactive browser popup; device-code
   fallback rendered as a QR/code card in the UI). The **entry (Entra) ID typed
   in the UI** pre-fills `az login --username` as a login hint.
2. Backend enumerates tenants (`az account tenant list` / ARM `/tenants`),
   surfaces them as checkboxes ("scan these 6 tenants").
3. Per selected tenant: `az account get-access-token --tenant <id>` for ARG;
   `--resource-type ms-graph` token for LAPS lookups. No client secret, no app
   registration needed for v1 — everything rides the user's az session.
4. All session state lives in the az CLI token cache; Stagecoach stores **zero
   credentials** (hard rule: no secrets in any file).

### 4.3 The connect click

1. Row click → drawer shows the methods the decision tree allowed, best first
   (Entra-auth Bastion RDP > Bastion RDP > Arc RDP-over-SSH > az ssh vm --rdp >
   tunnel > direct mstsc).
2. Windows target + local-user method → credential resolver kicks in
   (LAPS → Key Vault → prompt) and either pre-stages the clipboard or just lets
   the az prompt appear in the spawned terminal.
3. Backend spawns the command **detached in a new terminal window** (Windows
   Terminal profile if present) so interactive prompts/MFA work exactly as they
   do today; session registry records PID + target + method.
4. Tunnels get a free local port from a managed range; the registry shows
   "tunnel 127.0.0.1:5xxxx → vm:3389" with a copy button and a Stop.

### 4.3.1 Background & parallel session management

Every connection method keeps a **long-running helper process** alive for the
life of the session — `az network bastion tunnel` must keep running while mstsc
uses it, and `az ssh arc/vm --rdp` holds the SSH relay open behind the RDP
window. Today that means a leftover console window per session; Stagecoach
makes these first-class managed background processes instead:

- **Two spawn modes, chosen automatically per flow:**
  - **Hidden** (`Start-Process -WindowStyle Hidden`) for non-interactive
    helpers — tunnels and relays whose credentials/auth were fully staged by
    the resolver (Entra token, `--auth-type password` + stdin-fed password).
    No window ever appears; only mstsc (or the SSH terminal the user asked
    for) is visible.
  - **Minimized terminal** (`-WindowStyle Minimized`, Windows Terminal profile
    when present) for flows that may genuinely prompt (MFA challenges,
    first-run Arc service-config consent, unexpected password prompts). The
    sessions panel gets a **"Show window"** action that restores/focuses it
    when input is needed; it drops back to minimized afterwards. First-run
    consents are pre-answered where safe (`az ssh ... --yes` after the UI has
    explained what it grants).
- **Session registry** (backend, in-memory + `~/.stagecoach/sessions.json`
  without secrets): PID, child PIDs, target, method, local port, start time,
  captured stdout/stderr ring buffer (redacted) for the panel's "diagnose"
  view when a tunnel dies.
- **Parallelism:** sessions are independent jobs against a managed local port
  pool, so any number of simultaneous tunnels/RDP sessions run side by side;
  discovery scans already run per-tenant in parallel (`ForEach-Object
  -Parallel` with a throttle) and never block an active session.
- **Lifecycle & cleanup:** a watchdog health-checks each tunnel's local port;
  when the paired mstsc/ssh client exits, the helper is torn down
  automatically (orphan reaping); "Stop" in the panel kills the whole process
  tree; backend shutdown offers "close N active sessions" or leaves them
  running detached by explicit choice. Ports return to the pool on teardown,
  and a **Reconnect** action replays the same method/identity in one click.

### 4.4 Security posture

- Backend binds `127.0.0.1` only, random high port, per-launch bearer token
  injected into the SPA — nothing on the LAN can drive it.
- No passwords/tokens written to disk; clipboard auto-clear; logs redact secrets.
- Read-only against Azure (ARG/Graph/KV reads + connection establishment); any
  future write action (password reset, extension install) is explicit opt-in UI.
- Config file (`~/.stagecoach/config.json`): favorites, per-VM local-user
  overrides, tenant list, port range — no secrets, gitleaks-clean by design.

### 4.5 UX design (v1)

- **Sign-in card:** Entra ID (UPN) input → "Sign in with Microsoft" → tenant
  multi-select → **Scan**.
- **Estate grid:** one table, virtualized; columns: name, kind badge
  (`Azure VM` / `Arc` / `Azure Local`), OS icon, tenant, subscription, RG,
  power state, and **connect buttons** rendered per capability. Filters: text,
  tenant, kind, OS, "connectable only". Favorites pin to top.
- **Connect drawer:** method list with the *why* ("via bastion `bas-hub-01`",
  "Arc relay — no inbound ports"), identity choice (Entra vs local user),
  password source indicator (LAPS ✓ / Key Vault ✓ / will prompt).
- **Sessions panel:** live sessions/tunnels with stop buttons.
- **Empty/error states:** unreachable VM rows say why (no Bastion in VNet,
  Basic SKU bastion, agent offline, powered off) — honesty over silence,
  the azure-scout house rule.

## 5. Delivery status ledger (2026-08-23)

| Item | State |
|---|---|
| Repo `Hybrid-Solutions-Cloud/stagecoach` | ✅ Created (private per governance default), scaffolded: README, REPO-INTENT, AGENTS/CLAUDE, MIT LICENSE, `.ai/` workspace, this plan |
| Name & tagline | ✅ `stagecoach` · "One login. Every VM. One click." |
| Brand | ✅ Wagon-wheel mark (spokes = routes to VMs; ink ground, rust hub, rust rim accent) — `docs/public/images/stagecoach-icon.svg` (favicon + nav logo + hero) and `stagecoach-banner.svg` (README wordmark) |
| Docs site | ✅ VitePress coming-soon landing page; base `/stagecoach/`; publishes to the `gh-pages` branch via `.github/workflows/documentation.yml`; served at `labs.hybridsolutions.cloud/stagecoach/` through the org Pages custom domain |
| Docs CI runners | ⚠️ Temporarily `ubuntu-latest` (HCS self-hosted fleet offline); revert to `[self-hosted, linux, x64, hcs]` when the fleet returns |
| GitHub Pages | ⏳ Enable after first deploy: Settings → Pages → Deploy from branch → `gh-pages`. Note: Pages does not serve private repos on a free org plan — flip the repo public (fleet convention) or confirm plan support |
| ADO project | ✅ `HCS - Stagecoach` created via ADO REST (`wellFormed`), Agile process, matching the `HCS - <Product>` convention |
| HCS registry (`master-registry.db`) | ⏳ Row prepared, insert pending — see §5.1 |
| ADO Epic/Feature (AB#) | ⏳ Not yet created — needed before implementation commits |
| Research spikes & ADRs | ⏳ Requested; to be authored under `pmo/research/` and `docs/design/decisions/` (homestead-foundry format) |
| Docs About menu (About / Roadmap / Changelog / Releases) | ⏳ Requested; to follow the azure-scout `docs/project/` pattern |

### 5.1 Registry row (ready to insert)

The platform registry is SQLite at `mcp-server/data/master-registry.db` in the
Platform Engineering repo (the YAML registry is archived). Insert via the
`scripts/onboarding` tooling or sqlite3:

```sql
INSERT INTO repos (name, platform, org, local_path, type, validation_profile,
  default_branch, pilot, standards_scope, docs_platform, company, tenant,
  status, ado_project, notes)
VALUES ('stagecoach', 'github', 'Hybrid-Solutions-Cloud',
  'D:/git/hybrid-solutions-cloud/stagecoach', 'tool', NULL, 'main', 0, NULL,
  'vitepress', 'HCS Core', NULL, NULL, 'HCS - Stagecoach',
  'One-click Entra ID-authenticated RDP/SSH launcher for Azure VMs behind Bastion, Arc-enabled servers, and direct-reachable VMs. Created 2026-08-23; ADO project HCS - Stagecoach created the same day.');
```

## 5.2 Delivery plan

| Phase | Scope | Exit criteria |
|---|---|---|
| **0 — Spike** (repo bootstrap) | New repo `azure-stagecoach` from HCS template (`.ai/` workspace, standards, CI). Hand-run PS7 spike scripts proving all five command paths against real estate: bastion rdp, bastion tunnel, `az ssh arc --rdp`, `az ssh vm`, ARG sweep incl. multi-tenant token hop. | Each path demonstrated; quirks recorded in `.ai/memory/GOTCHAS.md`. |
| **1 — Core module** | `AzureStagecoach` PS module: `Connect-StagecoachAccount`, `Get-StagecoachInventory` (ARG + capability tree, JSON out), `Connect-StagecoachVM` (method selection + spawn), `Get-StagecoachCredential` (LAPS/KV resolver). Pester + PSScriptAnalyzer per HCS standards. | Full workflow usable from a terminal without the UI. |
| **2 — Local web UI** | Pode host + single-file React page (`stagecoach.html`, vendored UMD React + htm, no build step): sign-in, scan, grid, connect drawer, sessions panel. SSE for scan progress. Every connect click spawns `pwsh` running `Connect-StagecoachVM`. Background session manager: hidden/minimized helper spawning, port pool, watchdog, orphan reaping, reconnect. | Click-to-RDP works end-to-end for all three VM kinds with **no leftover console windows**; parallel sessions run side by side. |
| **3 — Credential polish** | Entra LAPS via Graph, Key Vault secret convention + `stagecoach-secret` tag override, opt-in save-to-vault write-back, clipboard auto-clear, per-VM local-user memory, favorites. | Windows local-admin flow needs zero manual password hunting where LAPS/KV exists. |
| **4 — Distribution** | PSGallery publish, `Start-Stagecoach` launcher, winget manifest, docs site (VitePress, labs.hybridsolutions.cloud/stagecoach). | Installable in one command on a fresh workstation. |

## 6. Prerequisites & constraints to document for users

- Azure CLI + `ssh` extension ≥ 2.0.4 (auto-installed on first `az ssh`), `bastion` commands need the `bastion` extension.
- Windows workstation for any `--rdp`/`az network bastion rdp` flow (macOS/Linux fall back to tunnels + own RDP client).
- Bastion **Standard SKU+** with native-client/tunneling enabled per bastion.
- Arc targets: connectivity endpoint + SSHD (WS2025 default; else `WindowsOpenSSH` extension).
- Entra RDP: client machine Entra-joined to the **same** tenant as the VM + `AADLoginForWindows`; Entra SSH: `AADSSHLoginForLinux` + VM Login role.
- LAPS retrieval: `DeviceLocalCredential.Read.All`-bearing role/scopes.

## 7. Open questions (for the operator)

1. Repo name sign-off: `azure-stagecoach` vs `azure-outrider` vs other.
2. v1 workstation target Windows-only (recommended), or macOS tunnel fallback in scope from day one?
3. Should Key Vault credential convention standardize on `kv-hcs-vault-01`, per-tenant vaults, or per-VM tags pointing at a secret ID?
4. Any tenants where PIM role activation is needed before scanning — should Stagecoach surface "activate PIM role" links in v1?
5. Create the ADO Epic/Feature for this (AB# for all future commits).

## 8. Sources

- az network bastion rdp/ssh/tunnel — learn.microsoft.com/azure/bastion/connect-vm-native-client-windows; /bastion/bastion-connect-vm-rdp-windows; /bastion/native-client
- Bastion Entra ID auth — learn.microsoft.com/azure/bastion/bastion-entra-id-authentication
- Arc SSH overview & enablement — learn.microsoft.com/azure/azure-arc/servers/ssh-arc-overview
- Azure Local VM SSH/RDP-over-SSH — learn.microsoft.com/azure/azure-local/manage/connect-arc-vm-using-ssh
- az ssh CLI reference (vm/arc/cert/config, `--rdp`) — learn.microsoft.com/cli/azure/ssh
- Entra SSH for Linux VMs — learn.microsoft.com/entra/identity/devices/howto-vm-sign-in-azure-ad-linux
- Windows LAPS + Entra — learn.microsoft.com/windows-server/identity/laps/laps-scenarios-azure-active-directory; /entra/identity/devices/howto-manage-local-admin-passwords; Get-LapsAADPassword reference
