# ADR-004: Persistent metadata cache and identity hub

- **Status:** Accepted
- **Date:** 2026-08-29

## Context

Operators manage multiple Entra identities and many subscriptions. A full network scan on every
launch prevents fast search and obscures identity-specific failures.

## Decision

Persist non-secret identity, scope, machine, access-path, favorite, recent, session-history, and
mapping metadata in local SQLite. The estate opens from cache and refreshes per identity in the
background or on demand. Azure remains authoritative and every row displays freshness/provenance.

Passwords remain in Windows Credential Manager and Azure tokens remain in each Azure CLI profile's
Windows-encrypted MSAL cache. Neither is copied into SQLite.

## Consequences

- Startup and search are immediate.
- Failure in one identity/tenant does not erase other cached results.
- Removing an identity can atomically remove its Azure CLI profile and related cached access paths.
