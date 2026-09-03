# Local accounts

Azure authorization and in-guest sign-in are different things. Your Entra account decides which
machines you can *see and reach*; a **local account** decides who you are *inside* the machine.

## What Stagecoach stores

| Item | Where it lives |
|---|---|
| Display name, username | Encrypted local SQLite metadata |
| Password | **Windows Credential Manager only** |
| Which account is pinned to which machine | Encrypted local SQLite metadata |

A password is never written to the Stagecoach database, a log, a command line, a process
argument, or an `.rdp` file. It is read from Windows Credential Manager at launch and discarded
when the session ends.

## Username format

- `username` — a machine-local account, such as the local administrator created when an Azure VM
  was provisioned (its `adminUsername`).
- `DOMAIN\username` — an Active Directory domain account.
- `user@domain` — also accepted for a domain account.

Stagecoach classifies the account from the format you type. There is no account-type dropdown to
get wrong.

## Pinning

**Edit** on any machine row opens a panel with a **Pinned local account** dropdown. Pinning writes
the choice immediately, so that machine connects on the first click and never asks again. You can
pin accounts across the estate before connecting to anything.

Leave the pin empty and the first connection asks once, then remembers.

The **Account** column on the machine list shows the pinned account, or **Ask** when there is none,
so it is always obvious which machines are set up.

## How the password reaches Remote Desktop

1. The password is read from Windows Credential Manager at launch.
2. It is staged as a temporary `TERMSRV/<endpoint>` credential through the Win32 credential API,
   so `mstsc.exe` does not show a logon box.
3. The temporary credential is deleted when the session ends.

For SSH, the password is handed to OpenSSH through the dedicated `Stagecoach.AskPass` helper on the
child process's own channel — never as an argument. SSH keys and Entra certificates are preferred
where the route supports them.

## Arc uses one account, not two

An Arc RDP session relays SSH and then runs Remote Desktop over it. Stagecoach uses **the same
pinned local account for both hops**. You are never asked to enter a local administrator account
for Arc, and Arc machines behave exactly like Azure VMs from the list.

## Removing an account

**Remove** deletes both the metadata and the Windows Credential Manager entry. Any machine pinned
to it reverts to asking, and Stagecoach tells you how many machines that affects.

## What Stagecoach does not do

- It does not manage, rotate, or retrieve managed local administrator passwords.
- It does not reset passwords in Azure. `az vm user update` is an Azure write and is outside
  Stagecoach's read-only discovery scope.
