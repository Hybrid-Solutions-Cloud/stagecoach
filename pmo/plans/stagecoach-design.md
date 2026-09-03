# Stagecoach native Windows design

**Status:** Accepted — supersedes the 2026-08-23 PowerShell/Pode/browser design
**Product owner clarification:** 2026-08-29
**Reference product:** `D:/git/hybrid-solutions-cloud/vault-prospector`

## 1. Outcome

Stagecoach is a native Windows command center for administrators. It combines the Azure estates
visible to multiple Microsoft Entra identities and turns each reachable machine into a deliberate,
repeatable, one-click RDP or SSH action.

The application must answer four questions without making the operator reconstruct context:

1. Which Azure identity can see and connect to this machine?
2. Which route reaches it: direct, Azure Bastion, Azure Arc, or Azure Local through Arc?
3. Which in-guest identity should be used for this environment?
4. Is the route ready now; if not, what exact prerequisite is missing?

## 2. Product model

### 2.1 Azure identity profile

An Azure identity profile is one human Entra account and its isolated Azure CLI state. It contains
only safe metadata:

- local profile ID and operator-chosen display name;
- Azure CLI configuration-directory path;
- account name returned by Azure CLI;
- authentication state and last successful validation time;
- discovered tenants and subscriptions;
- explicit enabled/disabled selection for every tenant and subscription.

Each profile receives its own `AZURE_CONFIG_DIR`. This is a supported Azure CLI isolation pattern
and avoids account switching and concurrent MSAL-cache writes. On Windows, current Azure CLI uses
Web Account Manager for interactive sign-in and encrypts its MSAL token cache. Stagecoach does not
read token files and does not import the user's default Azure CLI context.

Adding an identity launches `az login` with the isolated environment. The first option is **Use my
Windows account**, which allows WAM to select the current operating-system account. **Use another
account** presents the WAM account chooser. Reauthentication uses the same isolated profile.

No Stagecoach Entra application registration is required in v1. This avoids a second token cache
that Azure CLI extensions cannot consume and ensures discovery and connection use the same account.

### 2.2 Scope selection

After sign-in, Stagecoach reads Azure CLI account/tenant/subscription data and presents a tree:

```text
Identity
├─ Tenant A
│  ├─ Subscription 1  [selected]
│  └─ Subscription 2  [not selected]
└─ Tenant B           [not selected]
```

New tenants and subscriptions are not silently added to scan scope. They appear as review-needed.
Disabled scope remains cached but is excluded from new discovery runs.

### 2.3 Connection identity profile

Azure authorization and in-guest authentication are separate. A connection identity contains:

- display name;
- kind: Active Directory domain, machine-local, Microsoft Entra, SSH key, or prompt-only;
- username format (`DOMAIN\\user`, `user@domain`, `.\\user`, or SSH user);
- optional Windows Credential Manager target containing its protected password;
- optional SSH private-key path (the private key itself is never copied into Stagecoach state);
- mapping priority and safe match rules.

Match rules may target an exact machine, resource tag, detected AD DNS/NetBIOS domain, resource
group, subscription, or tenant. Most-specific wins; equal-rank ambiguity requires operator choice.
The chosen profile is shown before the first connection and may be overridden.

An Arc RDP route relays SSH and then runs Remote Desktop over it. **Both hops use the same single
connection identity.** The operator is never prompted to enter a local administrator account for an
Arc machine; the account's password feeds the OpenSSH AskPass helper for the relay, and the same
account is staged as a temporary `TERMSRV/localhost:<port>` credential so MSTSC does not prompt.

> **Amended 2026-09-02.** This section previously modelled the relay SSH identity and the Windows
> desktop identity separately. That produced two prompts for one Arc connection and is rejected.
> See `docs/design/decisions/ADR-005-pinned-local-accounts-and-single-arc-identity.md`.

Assignment is by **pinning**, not by rules. `Edit` on a machine pins one stored connection identity
to it; the estate list shows the pinned account, or `Ask` when there is none. An unpinned machine
asks once — a choice from the stored list, never typed credentials — and remembers the answer. The
scope-kind / match-value / priority / relay-flag mapping engine described in earlier revisions is
removed.

### 2.4 Operator profile

Stagecoach state belongs to the current Windows user. First launch creates the local operator
profile automatically. State under `%LOCALAPPDATA%/Stagecoach` is not portable to another Windows
account. Running inside an RDP session is supported; WAM/browser availability is diagnosed and a
device-code fallback is offered when interactive broker UI cannot be shown.

## 3. Discovery

### 3.1 Per-identity scan

For each enabled identity and tenant, Stagecoach queries only selected subscriptions. Runs are
isolated: one identity's expired authentication or denied subscription does not block another.
Existing cached results remain visible with freshness and error state.

Azure Resource Graph is used for scalable reads. The inventory graph includes:

- `Microsoft.Compute/virtualMachines`;
- `Microsoft.HybridCompute/machines`;
- `Microsoft.Network/bastionHosts`;
- NICs, IP configurations, public IPs, and virtual networks;
- VNet peerings in both directions;
- VM and Arc extensions relevant to Entra sign-in and OpenSSH;
- Hybrid Connectivity endpoint/service-configuration resources when visible.

### 3.2 Machine correlation

Every machine record retains provenance: identity, tenant, subscription, resource ID, resource
group, type, OS, location, state, tags, last scan, and per-source error. A physical Azure resource
seen by two identities has two access paths but one estate row. The row shows the preferred identity
and makes alternatives visible.

Azure VM to Bastion correlation follows network topology:

1. VM NIC to subnet and virtual network.
2. Bastion host to its `AzureBastionSubnet` virtual network.
3. Direct same-VNet reachability.
4. Supported VNet/global VNet peering reachability.
5. Bastion SKU and native-client/tunneling configuration.
6. Required read permissions on VM, NIC, virtual network, and Bastion.

The result is a capability, not a guess: Ready, Missing prerequisite, Permission unknown/denied,
Offline, or Unsupported.

### 3.3 Capability ordering

- Windows Azure VM: Bastion Entra RDP when eligible; Bastion tunnel + mapped Windows credential;
  direct RDP when explicitly enabled and an address is reachable.
- Linux Azure VM: Bastion/`az ssh vm` Entra SSH; configured SSH key; interactive fallback.
- Windows Arc/Azure Local: Arc RDP-over-SSH; Arc SSH; optional direct RDP.
- Linux Arc: Arc Entra SSH or configured SSH identity.

Powered-off Azure VMs and disconnected Arc agents remain visible with an explanation.

## 4. Connection orchestration

### 4.1 One-click contract

After onboarding and an unambiguous mapping, clicking **Connect**:

1. validates the selected Azure identity session silently;
2. selects the best ready route;
3. resolves relay and target identities;
4. retrieves any password from Windows Credential Manager only at launch;
5. stages an ephemeral `TERMSRV/<endpoint>` credential through the Win32 Credential API;
6. starts the Azure CLI helper/tunnel in the identity's isolated environment;
7. starts MSTSC or OpenSSH;
8. monitors both processes and local port;
9. removes the temporary endpoint credential and helper state;
10. records non-secret recent/session metadata.

Credentials are never placed in arguments. For OpenSSH password fallback, a dedicated AskPass
helper reads the selected Windows Credential Manager entry under the same Windows user and returns
it through the child process's standard channel. SSH keys and Entra certificates are preferred.

### 4.2 Authentication limitations

"One click" means Stagecoach does not add redundant prompts. It cannot suppress:

- Conditional Access or MFA challenges;
- expired/revoked Azure sessions requiring WAM or device-code interaction;
- Entra RDP prompts required by the Microsoft native-client flow;
- first-use trust/host-key decisions unless an approved known-host policy exists;
- target password changes outside the selected credential provider;
- consent for governed remediation.

These cases produce a focused action card rather than a hidden/minimized process waiting forever.

### 4.3 Process and session lifecycle

Helpers start with redirected output, no secret-bearing command line, and hidden windows when no
interaction is expected. A bounded redacted ring buffer supports diagnosis. The session registry
tracks process tree, endpoint, local port, route, start time, and health. It supports Stop, Show,
Copy endpoint, and Reconnect. Closing MSTSC/SSH reaps its helper and returns its port.

## 5. Governed Arc remediation

Stagecoach diagnoses separately:

- local Azure CLI version and `ssh`/Bastion command availability;
- Windows OpenSSH client availability;
- Arc connected-agent state;
- Hybrid Connectivity default endpoint;
- SSH service configuration on port 22;
- target SSH service/OpenSSH extension;
- Entra SSH extension and required roles when that route is selected.

Read-only diagnosis is automatic. A write action is never automatic. **Prepare connection** shows
the exact machine, Azure identity, subscription, commands/REST operations, expected effect, and
rollback/limitations. The operator must explicitly confirm. Execution is logged without tokens or
credentials and followed by a fresh readiness scan.

## 6. Desktop experience

> **Amended 2026-09-02.** Sections 6.1 and 6.2 previously specified a first-run setup wizard ahead
> of the estate. The application now opens **directly onto the machine list**; onboarding is done
> in the navigation sections, reached when needed. The rest of 6.2 is unchanged in intent.

### 6.1 Landing screen

The application opens on the machine list. An operator with no connected account sees the list
surface with an inline panel telling them to open **Connect identities** — not a wizard placed in
front of the product.

### 6.2 Machine list

The primary screen contains:

- persistent search;
- tenant, subscription, source, OS, and state filter dropdowns;
- Favorites / Ready only / Pinned quick toggles, and a Reset action;
- machine rows with favourite, name, source (Azure / Arc / Azure Local), OS, tenant, subscription,
  state, route, and the pinned local account (or `Ask`);
- a primary **Connect** action and an **Edit** action per row;
- a details panel for the selected machine covering access path, pinned account, readiness reason,
  and prerequisites.

`Edit` opens the per-machine panel: pin a local account, and override the route.

Navigation is limited to Machines, Connect identities, Local accounts, Sessions, and Settings, with
Machines first. It is rendered as a left product strip, not a tab bar.

### 6.3 Notification area and settings

Minimize-to-notification-area is on by default and the application keeps running. Because
Stagecoach owns live helper processes, **closing the window never terminates live sessions**
regardless of the configured close behaviour, and an explicit tray **Exit** requires confirmation
while any session is running. Tray actions are Show, a live session count, Sessions, Sync now, and
Exit.

Settings support System/Light/Dark theme, an accessible accent palette, start minimized, close
behavior, background refresh interval, workstation prerequisites, and software updates. Theme
values are design tokens; accessibility contrast is maintained.

### 6.4 Small screens and RDP sessions

The window has a minimum size of 320x300, compact control density, and flat opaque surfaces with no
corner radius, gradients, shadows, or transparency. This is a functional requirement, not a style
preference: the window must fit a laptop display and a windowed RDP session, and must compress
cleanly in a remote desktop bitmap cache.

### 6.5 In-app updates

Settings exposes check, verify, and install for new releases, accepted only from the signed
Stagecoach release repository. See
`docs/design/decisions/ADR-006-in-app-updates.md`.

## 7. Persistence and privacy

SQLite stores non-secret metadata. Windows Credential Manager stores target passwords. Azure CLI
stores its own Windows-encrypted token cache in each isolated profile. Settings and logs contain no
passwords or tokens. Local deletion supports removing one identity (including its isolated Azure
CLI cache), one connection credential, cached estate metadata, or all Stagecoach data.

## 8. Technical structure

```text
Stagecoach.App             Avalonia shell, setup, estate, tray, themes
Stagecoach.Core            Domain models and application contracts
Stagecoach.Infrastructure  SQLite, Azure CLI profiles, ARG discovery,
                           Credential Manager, process/session orchestration
Stagecoach.AskPass         narrow OpenSSH password bridge
Stagecoach.Tests           domain, persistence, parsing, routing, and security tests
installer/                 WiX v5 MSI
scripts/                   PS7 build, run, package, and validation commands
```

Target: .NET 10 LTS, Windows 10 19041+ x64, self-contained release packaging.

## 9. Required operator access

Stagecoach is for administrators who already have the required rights. Depending on route:

- Azure Resource Graph/read access to selected resources;
- read access to VM, NIC, VNet/peering, and Bastion;
- Azure Bastion Standard+ with native-client tunneling for native routes;
- Virtual Machine Administrator Login/User Login for Entra target sign-in;
- Arc Hybrid Connectivity and SSH permissions;
- in-guest Remote Desktop Users/administrator or SSH rights;
- write rights only for an explicitly confirmed remediation.

The app diagnoses missing rights but does not weaken them or silently elevate.

## 10. Acceptance

Release readiness requires a clean-machine MSI install and observable tests for multiple Entra
identities, per-identity scope, cached combined estate, Bastion correlation, Arc/Azure Local
inventory, domain/local credential mapping, direct/Bastion/Arc launch paths, RDP-session operation,
notification-area behavior, themes, redaction, removal, and governed remediation preview. Live
Azure-dependent paths remain unproven until executed against representative authorized targets.
