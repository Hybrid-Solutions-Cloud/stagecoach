# Handoff

**Session:** 2026-09-04 · **Branch:** `main` · **Head:** `c2044c0` · **Released:** v0.6.1

## What changed

**Stagecoach no longer has an application passphrase.** The operator rejected it outright — *"I did
not do this for prospector. I am not doing this for this app."* — after 0.5.0 added an optional
passphrase lock and 0.6.0 made one mandatory.

Vault Prospector was read properly this time rather than guessed at. It has **no typed secret**: the
database key comes from `WindowsDataProtectionKeyProvider` (DPAPI, Windows-account bound), and
unlock is a presence check — `UserConsentVerifier`, falling through to
`CredUIPromptForWindowsCredentials` + `LogonUser` + a SID comparison when Hello cannot prompt, which
inside a remote session it never can. Stagecoach now matches that.

## Files touched

- `src/Stagecoach.App/Security/AppOwner.cs` — passphrase removed; `owner.json` v2; legacy read kept
  only for migration.
- `src/Stagecoach.App/Security/WindowsCredentialVerifier.cs` — **new**, ported from Prospector.
  Synchronous on the UI thread on purpose (modal Win32 dialog).
- `src/Stagecoach.App/Security/LocalState.cs` — **new**, backs "Start fresh".
- `src/Stagecoach.App/Views/UnlockWindow.axaml{,.cs}` — verification-only, auto-prompts on open.
- `src/Stagecoach.App/Views/PassphraseRemovalWindow.axaml{,.cs}` — **new**, the one-time migration.
- `src/Stagecoach.App/Views/OwnerSetupWindow.axaml{,.cs}` — passphrase fields removed.
- `src/Stagecoach.App/AppLock.cs` — **deleted**, with its Settings UI.
- `src/Stagecoach.App/App.axaml.cs` — new startup gate with the interrupt-safe migration probe.
- `docs/about/changelog.md`, `docs/about/releases.md` — filled in 0.3.0 → 0.6.1 (were stuck at 0.2.0).

## Bug found and fixed on the way

`AppLock.Enable` rewrapped the metadata key with entropy derived from its own passphrase, while
startup after 0.6.0 supplied only `AppOwner`'s. **Enabling the Settings lock would have made the
database key impossible to unwrap on the next launch.** Deleted rather than repaired.

## Commands run

- `dotnet build` — clean, 0 warnings.
- `dotnet test` — **81 passed**, including `RemovingTheLegacyPassphraseLeavesTheEstateReadable`,
  which exercises the migration against real DPAPI plus the interrupted case.
- `dotnet format --verify-no-changes` — clean.
- `./scripts/Package.ps1 -Version 0.6.1 -Installer`; release published via the GitHub App token,
  install ID **131587716**.

## Not verified

First-run setup, the Windows Hello prompt, the Windows credential prompt, and the Entra owner
sign-in have not been driven by hand — they need an interactive session, and the operator's own
machine is never to be touched.

**Live Azure connection validation remains the standing gap:** Bastion tunnels, Arc RDP-over-SSH,
and `TERMSRV` credential staging have never been exercised against a real machine.

## Next

1. Operator updates in-app to 0.6.1 and confirms the unlock behaves.
2. Live connection validation.
3. Authenticode signing so `RequireProvenanceBundle` can return to `true`.
