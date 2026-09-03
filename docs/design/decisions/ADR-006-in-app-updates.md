# ADR-006: In-app updates

**Status:** Accepted
**Date:** 2026-09-02

## Context

Stagecoach is installed per workstation from an MSI. Without an in-application path, operators stay
on whatever version they first installed, and a withdrawn or defective release has no route to
being replaced.

Downloading and executing an installer is the most dangerous thing a desktop application can do on
its own, so the mechanism has to be conservative by construction rather than by convention.

## Decision

Adopt the Vault Prospector update model, unchanged in substance.

A release is a candidate only when all of the following hold: it is published in the Stagecoach
release repository; it is not a draft; its author is the expected publishing app; neither its name
nor its notes are marked withdrawn; it carries the MSI, its SHA-256 sidecar, and a Sigstore bundle;
every asset URL is absolute HTTPS inside that repository's release download prefix; and the digest
GitHub reports is a well-formed SHA-256 within size bounds.

Versions are compared with a semantic-version comparer that orders prerelease identifiers
correctly. A build whose own version does not parse is reported as a development build and never
self-updates.

The download is streamed through an incremental hash, rejected if it exceeds the authenticated
size or ends short, written to a temporary partial path, and compared in fixed time against both
the sidecar and the authenticated digest before being moved into place. The update directory is
confined and rejected if it is a reparse point. The installer is hashed **again** immediately
before launch, then handed to Windows Installer with the runas verb, so elevation is the operating
system's own prompt.

## Consequences

- A tampered, truncated, substituted, or swapped-after-verification installer will not run.
- A bad release can be withdrawn by editing its title or notes; clients stop offering it.
- The release pipeline **must** publish all three assets under the publishing app identity. Until
  it does, update checks correctly report that no trusted release was found. This is a pipeline
  obligation, not a defect to be worked around by relaxing the checks.
- Code signing and public release-channel ownership remain open and are tracked separately.
