# ADR-001: Native Windows application

- **Status:** Accepted
- **Date:** 2026-08-29

## Context

Stagecoach must behave like Vault Prospector: an installed operator application with persistent
identity scope, instant cached search, background sessions, notification-area lifecycle, and
Windows-native credential/process integration. The original localhost PowerShell/Pode/browser
design and a later incomplete WPF prototype left two incompatible product shapes in the repo.

## Decision

Stagecoach is an Avalonia desktop application targeting .NET 10 LTS and Windows 10 19041 or later.
It uses the same layered desktop pattern as Vault Prospector while keeping Stagecoach-specific
Azure CLI connection orchestration. The PowerShell/Pode/browser product path is retired.

## Consequences

- Windows Credential Manager, MSTSC, OpenSSH, notification-area, and RDP-session behavior can be
  implemented and tested directly.
- The app can be self-contained and packaged as an MSI.
- Windows is the v1 product boundary.
- PowerShell remains only for governed build/packaging automation.
