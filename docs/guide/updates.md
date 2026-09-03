# Updating Stagecoach

Stagecoach checks for, verifies, and installs its own updates. **Settings → Software updates.**

## Using it

1. **Check for updates** — reads the signed release feed and reports the newest trusted release.
2. **Download and verify** — downloads the MSI and verifies it before it is allowed to run.
3. **Install update** — hands the verified MSI to Windows Installer. The Windows elevation prompt
   is Windows's own, so you always see what is being installed.

A development build reports itself as such and will not self-update.

## What "trusted" means

An update is only offered when every one of these holds:

- the release is published in the Stagecoach release repository, not a fork or a mirror;
- it is not a draft, and its author is the expected publishing app;
- neither its name nor its notes are marked withdrawn — that is the kill switch for a bad release;
- it carries all three assets: the `.msi`, its `.sha256` sidecar, and a Sigstore bundle;
- every asset URL is absolute HTTPS inside that repository's release download path;
- the asset digest GitHub reports is a well-formed SHA-256 and the size is within bounds.

Anything else is ignored rather than downgraded to a warning.

## What verification does

The download is streamed and hashed as it arrives. It is rejected if it exceeds the authenticated
size, if it ends short, or if the hash does not match — compared in fixed time against both the
`.sha256` sidecar and the digest GitHub authenticated. The file is written to a temporary partial
path and only moved into place after it verifies.

The installer is hashed **again** immediately before launch, so a file swapped between
verification and execution is caught. The update directory is confined and rejected if it is a
reparse point.

::: info Release pipeline requirement
Because the Sigstore bundle is mandatory, the release pipeline must publish
`Stagecoach-<version>-win-x64.msi`, `Stagecoach-<version>-win-x64.msi.sha256`, and
`Stagecoach-<version>-win-x64.msi.sigstore.json` under the publishing app identity. Until a
release with all three exists, checking correctly reports that no trusted release was found.
:::
