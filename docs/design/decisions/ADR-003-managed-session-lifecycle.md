# ADR-003: Managed background session lifecycle

- **Status:** Accepted
- **Date:** 2026-08-29

## Context

Bastion tunnels and Arc SSH relays must remain alive while RDP/SSH is active. Unmanaged CLI
windows create orphan processes, occupied ports, hidden prompts, and uncertain cleanup.

## Decision

Stagecoach owns every helper/client process as a session. It allocates a local port, captures a
bounded redacted diagnostic buffer, monitors process and port health, and cleans up helpers,
temporary endpoint credentials, and ports when the client exits. Sessions expose Stop, Show,
Diagnose, and Reconnect. Hidden helpers are allowed only when no interaction is expected.

## Consequences

- Parallel sessions can run safely.
- A process waiting for MFA, host-key acceptance, or consent becomes an actionable UI state.
- Notification-area/background operation is part of the connection architecture, not cosmetic UI.
