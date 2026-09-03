# ADR-005: Pinned local accounts and a single Arc identity

**Status:** Accepted
**Date:** 2026-09-02
**Supersedes:** the mapping-rule model and the two-identity Arc model in
[ADR-002](./ADR-002-identity-and-credential-separation.md) and `pmo/plans/stagecoach-design.md` section 2.3

## Context

The first implementation matched a local account to a machine with a rule engine: a scope kind
(tenant, subscription, resource group, domain, tag, machine), a match value, a numeric priority,
and a separate flag marking a mapping as the Arc SSH relay identity.

Two problems followed.

First, the operator had to reason about precedence to answer a simple question — which account does
this machine use? The answer was computed from rules rather than stated.

Second, because the relay identity and the desktop identity were modelled separately, an Arc
Remote Desktop session could ask for a local administrator account twice: once for the SSH hop and
once for the Windows sign-in. That is exactly the redundant prompting Stagecoach exists to remove.

## Decision

**Pin a local account directly to a machine.** Edit on a machine row sets it; the machine list
shows it in an Account column, or "Ask" when there is none. An unpinned machine asks once, from a
list of stored accounts, and remembers the answer.

**Arc uses that one account for both hops.** Its password feeds the OpenSSH AskPass helper for the
relay, and the same account is staged as a temporary endpoint credential so Remote Desktop does not
prompt.

The mapping rule builder is removed.

## Consequences

- The account a machine will use is stored data, visible in the list, not a computed result.
- Arc and Azure machines behave identically at the point of use.
- An operator can pin accounts across the estate before connecting to anything.
- Bulk assignment by tag, domain, or resource group is no longer expressed as a rule. If it is
  needed later it should arrive as a multi-select "pin to selected machines" action, which still
  produces explicit per-machine pins rather than precedence to reason about.
- Credentials are still never typed at connect time; picking from stored accounts is the only path.
