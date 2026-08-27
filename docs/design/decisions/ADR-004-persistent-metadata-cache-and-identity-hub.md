# ADR-004: Persistent Metadata Cache and Multi-Identity Hub

- **Status:** Accepted
- **Date:** 2026-08-27
- **Deciders:** Kristopher Turner, Stagecoach Engineering
- **Technical Context:** Desktop Command Center & Metadata Lifecycle (modeled after Vault Prospector)

---

## Context and Problem Statement

In the initial prototype, Stagecoach executed an Azure Resource Graph (ARG) sweep across subscriptions every time the operator requested an inventory view. For operators managing dozens of subscriptions across multiple Entra ID tenants, this created unwanted latency and required waiting on network calls before searching or connecting.

Furthermore, operators often manage workloads under multiple distinct Entra ID identities (e.g. corporate UPN vs. customer lab credentials).

---

## Decision Drivers

- **Instant Search:** VMs across all accessible tenants must be searchable in sub-milliseconds upon opening the app.
- **Multi-Identity Support:** Enable connecting multiple Entra ID accounts and selecting which tenants to include in the sync.
- **Local Persistence:** Retain discovered VM metadata, favorites, and recent connection history across application restarts.
- **Zero Secret Storage:** Metadata persistence must strictly store non-secret machine attributes (Name, Resource Group, OS, Domain status, Resource ID), never passwords or access tokens.

---

## Decision Outcome

Adopt the **Vault Prospector application pattern**:

1. **Multi-Identity Hub:** A dedicated **Identities** view manages connected Entra accounts and allows selective tenant synchronization.
2. **Persistent Local Metadata Cache:** Discovered VM metadata is stored in local persistent storage (`~/.stagecoach/inventory.json` and browser storage).
3. **Estate Command Center:** The **Estate** view operates against the local cache, providing instantaneous filtering, favorites pinning, and recents tracking.
4. **On-Demand Background Sync:** An explicit **"Sync Estate"** action sweeps Azure Resource Graph in the background and updates the local store without blocking UI navigation.

---

## Consequences

- **Positive:** Zero latency when launching the app or searching for machines.
- **Positive:** Support for favorites, recents, and multi-tenant grouping.
- **Positive:** Complies with HCS security rules because all cached data is non-sensitive metadata.

