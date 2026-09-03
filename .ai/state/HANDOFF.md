# Handoff

## Session 2026-09-02 — interface rebuilt around the operator workflow

### Branch

`main`, from `7b615f4`.

### Why

The delivered application built cleanly but did not match the accepted design's UX section or the
way the operator actually works. Audit findings that drove this session: no landing behaviour on
the estate, no details panel, search-only filtering with none of the required filters, the
remediation preview parked in Settings, and a mapping rule engine standing between the operator and
a connection. The window also had a 1080x680 minimum, which cannot fit a laptop display or a
windowed RDP session.

### Delivered

- **Machines is the landing screen.** The application opens on the machine list. An operator with
  no account connected gets an inline pointer to Connect identities instead of a wizard.
- **Real filtering.** Tenant, subscription, source (Azure / Arc / Azure Local), OS, and state
  dropdowns, plus Favorites / Ready only / Pinned toggles, a search box, and Reset. Tenant and
  subscription are now columns as well as filters.
- **Pinned local accounts.** `Edit` on a machine pins a stored account, so that machine connects on
  the first click. Unpinned machines ask once, from a list, and remember. Credentials are never
  typed at connect time. New `MachinePins` table with cascade delete from `ConnectionIdentities`.
- **One account for both Arc hops.** `MainViewModel.LaunchAsync` passes the same account as target
  and relay, so an Arc RDP session never prompts for a local administrator account twice. This
  supersedes design section 2.3; ADR-005 records the decision.
- **Local accounts** replace the mapping-rule builder. Account type is inferred from the username
  format rather than a dropdown.
- **Session-aware lifecycle.** New `WindowLifecyclePolicy` holds the decisions as pure statics.
  The tray shows a live session count, Exit requires confirmation while sessions run, and closing
  the window never tears down live sessions regardless of the close behaviour setting.
- **In-app updates.** `GitHubReleaseUpdateService` ported from Vault Prospector with every control
  intact: publisher check, mandatory Sigstore bundle, withdrawn-release kill switch, trusted URI
  prefixes, streamed incremental hashing against the authenticated digest, contained update
  directory, reparse-point rejection, and a second hash immediately before launch. ADR-006.
- **Shell rebuilt on the Prospector pattern.** Full design-token set, `TabControl.product-shell`
  left navigation, header band with an active-account context strip, an accessible error banner,
  and a status bar. The machine list is an aligned `ListBox` rather than a `DataGrid`.
- **Laptop and RDP fitness.** Minimum window size 320x300, compact density, flat opaque surfaces
  with no corner radius, and every screen inside a scroll viewer.
- Documentation rewritten to match, including new interface, updates, download, and scripts pages,
  ADR-005 and ADR-006, and an amendment note on ADR-002. Six dead ADR navigation entries left over
  from the superseded PowerShell design were removed.

### Verification

| Check | Result |
|---|---|
| `dotnet build Stagecoach.sln -c Release` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Stagecoach.sln -c Release` | 39 passed, 0 failed (was 12) |
| `dotnet format --verify-no-changes --no-restore` | Clean |
| `dotnet list package --vulnerable --include-transitive` | None across all five projects |
| `git diff --check` | Clean |
| `npm run docs:build` | Build complete, no dead links |
| Application launch | Window titled `Stagecoach`, responsive, no exceptions |
| `scripts/Package.ps1 -Version 0.1.0 -Installer` | ZIP, SHA-256 sidecar, and MSI produced |

### Gotcha worth remembering

Packaging failed with `Access to the path 'Avalonia.Base.dll' is denied` because a stale
`Stagecoach.App.exe` from a previous session was still running out of `artifacts/publish-win-x64`.
Stop any running instance before packaging.

### Not done

- **No live Azure validation.** Entra sign-in, subscription discovery, Bastion correlation, Arc and
  Azure Local routes, credential staging, Conditional Access behaviour, and OpenSSH deployment all
  require representative authorized resources. A green build is not evidence for any of them, and
  no work item should be closed on it.
- **No GitHub release exists yet**, so the download page's `releases/latest` links will 404 until
  one is published.
- The in-app updater requires the release pipeline to publish the MSI, its `.sha256` sidecar, and a
  `.sigstore.json` bundle under the publishing app identity. Until then, update checks correctly
  report that no trusted release was found. Do not relax the checks to work around this.

### Next

1. Publish the 0.1.0 release with the MSI, ZIP, and checksums.
2. Stand up the `stagecoach-releases` publishing pipeline including Sigstore bundles.
3. Run the live validation matrix in `pmo/plans/stagecoach-implementation-plan.md`.
