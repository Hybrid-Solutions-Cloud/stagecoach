# ADR-002: Separate Azure identities from target connection identities

- **Status:** Accepted
- **Date:** 2026-08-29
- **Amended:** 2026-09-02 — the mapping-rule engine and the two-identity Arc model are superseded by [ADR-005](./ADR-005-pinned-local-accounts-and-single-arc-identity.md). The separation of Azure identities from in-guest accounts, which is the substance of this ADR, still stands.

## Context

An Entra identity authorizes Azure discovery and relay creation. A domain/local/Entra target
identity authenticates inside the VM. They frequently differ, and Arc RDP can additionally require
a local SSH relay identity.

## Decision

Model Azure identities, relay identities, and target connection identities separately. Azure
identities use isolated Azure CLI profiles. Target passwords use Windows Credential Manager;
usernames, SSH key paths, and mapping rules are non-secret SQLite metadata. Mappings may target
machine, tag, domain, resource group, subscription, or tenant, with deterministic specificity.

## Consequences

- One Entra account can connect using different AD domain accounts across environments.
- Multiple Entra accounts can use the same target identity without duplicating its password.
- Ambiguous mappings are visible instead of silently selecting a credential.
- Secrets never enter SQLite, JSON settings, logs, command arguments, or RDP files.
