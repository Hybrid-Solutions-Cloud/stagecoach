# Operator backlog — reported 2026-09-03

Everything the operator has reported or requested in this session, with status. Bugs first, then
requested features, then open questions. Nothing here is closed on a green build alone.

## Bugs — fixed and released

| # | Report | Cause | Released |
|---|---|---|---|
| 1 | Interface is confusing; nothing like Vault Prospector | The shipped build copied Prospector's tab list but none of its shell — no design tokens, no product navigation, no context strip, no error banner, bare `DataGrid` | 0.2.0 |
| 2 | Window unusable on a laptop and inside RDP | `MinWidth` 1080 x 680; rounded corners and shadows that recompress badly over RDP | 0.2.0 |
| 3 | Application icon was a generic "command box" | No `<ApplicationIcon>` at all; the only icon was a base64 blob in a source file | 0.2.2 |
| 4 | Add/Remove Programs showed a generic icon | WiX `Icon` id must end in `.ico`, and `ARPPRODUCTICON` alone leaves `DisplayIcon` empty | 0.2.2 |
| 5 | "Read-only" error, no detail, no record | No writability preflight, WAL assumed available, storage errors collapsed into "unexpected local error", no crash log anywhere | 0.2.2 |
| 6 | Could not add an account | `FileName = "az"` with `UseShellExecute=false` — CreateProcess does no PATHEXT resolution, so **the Azure CLI could never be launched at all** | 0.2.3 |
| 7 | Sign-in hung with no feedback | Interactive commands captured no output and had no timeout; device codes went to a hidden console | 0.2.3 |
| 8 | Installer showed only a UAC prompt and a flicker | The MSI had no `WixUI` element at all | 0.3.0 |
| 9 | No idea what to do on first run | No first-run guidance; the machine list was simply empty | 0.3.0 |
| 10 | "Use my Windows account" is wrong — mine is not an Entra account | Wording implied the Windows sign-in must be an Entra account | 0.3.0 |
| 11 | "Updates have not been checked" never cleared | Updater pointed at `stagecoach-releases`, which does not exist; required a provenance bundle no release carries; never updated the panel on failure | 0.3.0 |
| 12 | Buttons vanished while typing an account name | Startup blocked the UI for ~20 s on Azure CLI readiness checks, and disabled styling faded controls to near-invisible | 0.3.1 |
| 13 | Taskbar icon still generic | Explicit `AppUserModelID` makes the taskbar resolve its icon through a Start-menu shortcut that declared it — never true for the portable ZIP | 0.3.1 |
| 14 | Adding an account failed every time after a successful sign-in | Profile promotion created the destination directory and then moved onto it — guaranteed `IOException` | 0.3.2 |
| 15 | Buttons disappeared on hover and never came back | Avalonia 12 Fluent paints a content presenter over the button and owns the hover brushes | 0.3.2 |
| 16 | "Already configured", but the account was never listed | Account saved, then subscription enumeration threw, so the list never reloaded — and the duplicate check then blocked every retry | 0.3.3 |
| 17 | Left navigation items disappeared under the pointer | Fluent resolves state colours by **resource key**, not the control's `Background`; selected white text met the theme's light hover brush | 0.3.4 |

## Bugs — fixed, pending release in 0.4.0

| # | Report | Cause |
|---|---|---|
| 18 | Selecting an account blanks its card | Same resource-key problem on `ListBoxItem`: the theme's white selected-row foreground on a white card |
| 19 | Tenant and subscription rows disappear on hover | Same, for those lists |
| 20 | Lists are clipped, no visible scroll bar, poor alignment | Overlay scroll bars auto-hide; rows had no fixed column layout |
| 21 | **Azure Resource Graph discovery failed for every identity** | The KQL was passed in `--query`, which is Azure CLI's **global JMESPath output filter**. Correct switch is `--graph-query`. Verified: `--query` returns `invalid jmespath_type value`, `-q` succeeds. **Discovery has never worked.** |
| 22 | Scope was not enumerated when an account was added | Enumeration failure inside add was swallowed; now retried and surfaced |
| 23 | Updating while the app is open is painful | MSI cannot replace a running executable; now closes it via `util:CloseApplication` and the app steps aside after elevation |
| 24 | "Active account" is misleading | It showed a discovery identity, implying an application login; relabelled and the real protection model documented in Settings |

## Requested features

| # | Request | Status |
|---|---|---|
| A | Support-bundle / log collection like Prospector | **Done** — 0.3.1, Settings → Support and diagnostics |
| B | Rename a connected account | **Done** — pending release in 0.4.0 |
| C | Include all / exclude all for tenants and subscriptions | **Done** — pending release in 0.4.0 |
| D | Force-close or silently update while running | **Done** — pending release in 0.4.0 |
| E | Show when a scan last ran and what it found; an audit log | **Not started** |
| F | Export and import settings to move to another laptop | **Not started** |
| G | Application-level protection like Prospector's secure unlock | **Not started** — see below |

### H — Quick Connect (requested 2026-09-03, not started)

A one-off connection that saves nothing. Button opens a short prompt chain:

1. Authenticate to a tenant.
2. Ask for a subscription — **optional**.
3. Ask whether the target is reached through **Azure Bastion** or **Azure Arc**.
4. Ask for a resource name.
5. If it is an Arc machine, ask for the local account.
6. Connect.

Fallbacks:
- No subscription given → scan the tenant's subscriptions, then look for the resource name across them.
- No resource name given → list every Azure VM, or every Arc machine, depending on the choice at
  step 3, and let the operator pick.

Nothing is persisted: no identity, no pin, no scope, no estate row. It is a throwaway path beside
the saved estate, for reaching something once without setting it up.

### I — Tenant exclusion cascades to subscriptions (fixed, pending release)

Excluding a tenant now greys out its subscriptions, marks them "Tenant excluded", disables their
toggle, and — the part that actually mattered — removes them from what discovery scans. Previously
`DiscoverAsync` filtered only on the subscription's own flag, so a subscription left enabled under
an excluded tenant would still have been queried.

## Open questions

**G — application sign-in.** Stagecoach currently has no unlock of its own. Its data is bound to the
Windows account: SQLCipher database with a DPAPI `CurrentUser` key, local passwords in Windows
Credential Manager, Azure tokens in each account's isolated Azure CLI cache. Another Windows user
cannot read it and the files are useless on another machine — but anyone at an unlocked session can.
Vault Prospector adds an explicit unlock gate. Whether Stagecoach should is an open product decision.

**E — audit log.** Should cover: scan start and finish per identity, counts discovered, errors, and
connection attempts. Deliberately excludes credentials and tokens. Home is likely a new page rather
than Settings.

**F — export/import.** Portable content: connection identities without passwords, machine pins,
scope selection, settings. Must **never** include the SQLCipher key, Credential Manager entries, or
Azure token caches, since those are machine and user bound by design.

## Not yet proven

No live connection to a machine has been made — Bastion, Arc RDP-over-SSH, and `TERMSRV` credential
staging are all unexercised. With discovery fixed in 0.4.0, this is the next thing to validate.
