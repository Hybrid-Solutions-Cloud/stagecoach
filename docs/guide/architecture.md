# Architecture Overview

Stagecoach is designed as a **local-first, zero-infrastructure application**. It bridges your browser directly to native workstation processes without requiring cloud servers, multi-tenant databases, or Node.js compilation steps.

---

## High-Level Architecture

```
┌──────────────────────────────── Operator Workstation ────────────────────────────────┐
│                                                                                      │
│   Single-File React UI (http://127.0.0.1:8085/)                                      │
│   ├── Static HTML + Vendored React UMD + htm                                         │
│   ├── Estate Grid (Resource Graph inventory + Domain badges)                         │
│   └── Slide-Over Connect Drawer (Credential resolver status)                         │
│                           │                                                          │
│                           │ Local HTTP (GET /api/inventory, POST /api/connect)       │
│                           ▼                                                          │
│   PowerShell 7 Localhost Server (Start-Stagecoach)                                   │
│   ├── Discovery Engine: Queries Azure Resource Graph (ARG)                           │
│   ├── Credential Resolver: Resolves LAPS, Domain defaults & Key Vault secrets        │
│   └── Process Launcher: Detached process spawning of az & mstsc.exe                  │
│                           │                                                          │
└───────────────────────────┼──────────────────────────────────────────────────────────┘
                            │ HTTPS (Operator's az login session)
                            ▼
           Azure ARM / Resource Graph / Key Vault / Arc Relay
```

---

## Core Design Principles

### 1. Zero Infrastructure & Local-Only Execution
- Stagecoach runs entirely on the operator's machine.
- The web server binds strictly to `127.0.0.1`.
- No server-side components, Docker containers, or cloud VMs need to be deployed.

### 2. Single-File Frontend
- The user interface (`stagecoach.html`) is built using vendored React UMD bundles and `htm` tagged template literals.
- **No Node.js or Webpack required** on the operator's workstation.

### 3. Native Subprocess Orchestration
- Web browsers cannot directly invoke desktop programs like `mstsc.exe` or `az.exe`.
- When an action is taken in the browser, it sends a command to the local PowerShell 7 listener, which executes the corresponding `AzureStagecoach` cmdlet.

### 4. Zero Credential Persistence
- Passwords and tokens are **never written to disk**, cookies, or local storage.
- Passwords are held transiently in memory as secure strings only for the seconds required to stage the session.

