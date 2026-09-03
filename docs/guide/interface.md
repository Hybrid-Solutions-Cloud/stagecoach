# The interface

Stagecoach is one window with five places in it. Only the first one is used day to day.

```
┌──────────────────────────────────────────────────────────────────────┐
│  S  Stagecoach                    READ-ONLY DISCOVERY  [↻ Sync]      │  header
├──────────────────────────────────────────────────────────────────────┤
│  ACTIVE ACCOUNT   ▌you@contoso.com                    148 machines   │  context strip
├────────────────┬─────────────────────────────────────────────────────┤
│ ▤ Machines     │  AZURE ESTATE                                       │
│ ◎ Connect ids  │  Pick a machine                                     │
│ ◉ Local accts  │  ┌─ search ────────────────────────────────────┐    │
│ ⌁ Sessions     │  │ Tenant ▾  Subscription ▾  Source ▾  OS ▾    │    │
│ ⚙ Settings     │  ├─────────────────────────────────────────────┤    │
│                │  │ ★ VM-DC01  Azure  Windows  … Bastion  Ready │    │
│                │  │   ARC-FS02 Arc    Windows  … Arc RDP  Ready │    │
│                │  └─────────────────────────────────────────────┘    │
├────────────────┴─────────────────────────────────────────────────────┤
│  Connecting to VM-DC01 — starting Bastion tunnel…       2 active     │  status bar
└──────────────────────────────────────────────────────────────────────┘
```

The window opens straight onto **Machines**. There is no wizard and no dashboard in front of it.

## Machines

A filterable list of everything your connected accounts can reach.

| Column | What it tells you |
|---|---|
| ★ | Favourite. Favourites sort to the top. |
| Machine | Resource name. |
| Source | **Azure**, **Arc**, or **Azure Local**. |
| OS | Windows or Linux. |
| Tenant | Which Entra tenant it lives in. |
| Subscription | Which subscription it lives in. |
| State | Power state for Azure VMs, agent state for Arc machines. |
| Route | The route Stagecoach will use: Bastion tunnel, Direct RDP, Arc RDP, and so on. |
| Account | The pinned local account, or **Ask** if none is pinned yet. |

Filter with the search box, the quick toggles (Favorites, Ready only, Pinned account), and the
Tenant / Subscription / Source / OS / State dropdowns. **Reset** clears all of them.

**Connect** launches the session. **Edit** opens the per-machine panel where you pin a local
account and override the route.

Selecting a row also opens a detail panel below the list showing the tenant, subscription, route,
pinned account, and — when the machine is not ready — exactly what is missing.

## Connect identities

Where Microsoft Entra accounts are added. Each account gets its own isolated, Windows-encrypted
Azure CLI session; your own `~/.azure` profile is never touched.

Two separate actions live here, and they do different things:

- **Refresh available scope** — re-enumerates tenants and subscriptions to find newly granted
  access. Anything new stays excluded until you include it.
- **Rescan machines** — re-reads the estate inside the scope you have already included.

## Local accounts

The accounts used *inside* the machines. Username is `DOMAIN\username` for a domain account or
plain `username` for a local one. Passwords go to Windows Credential Manager, never to the
Stagecoach database, logs, or command lines.

## Sessions

Live RDP, SSH, Bastion, and Arc helper processes, with a Stop button each. Closing a Remote
Desktop or SSH window reaps its helper automatically and returns its local port.

## Settings

Theme and accent, close and minimize behaviour, background refresh interval, workstation
prerequisites, and [software updates](./updates.md).

## Notification area

Minimizing sends Stagecoach to the notification area and it keeps running — deliberately, because
it owns your live sessions. The tray menu shows how many sessions are running, and **Exit** asks
for confirmation while any are still open. Closing the window with sessions running never exits,
whatever the close behaviour is set to.

## Small screens and RDP

The window shrinks to 320 × 300, uses compact density, and is drawn with flat, square, opaque
surfaces. That is deliberate: it fits a laptop screen, fits inside a windowed RDP session, and
compresses cleanly over a remote desktop connection instead of smearing.
